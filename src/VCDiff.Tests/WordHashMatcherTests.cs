using System;
using System.IO;
using System.Threading.Tasks;
using VCDiff.Decoders;
using VCDiff.Encoders;
using VCDiff.Includes;
using VCDiff.Shared;
using Xunit;

namespace VCDiff.Tests
{
    public class WordHashMatcherTests
    {
        [Theory]
        [InlineData(8, 255)]
        [InlineData(8, 256)]
        [InlineData(16, 65535)]
        [InlineData(16, 65536)]
        [InlineData(32, 300)]
        public async Task CopyNearEndOfDictionary(int blockSize, int blocks)
        {
            byte[] source = new byte[blockSize * blocks + 3];
            new Random(19).NextBytes(source);
            byte[] target = new byte[Math.Max(32, 4 * blockSize) + 3];
            Array.Copy(source, source.Length - target.Length, target, 0, target.Length);
            using var dictionary = new ByteBuffer(source);
            using var input = new MemoryStream(target);
            using var patch = new MemoryStream();
            using var encoder = new VcEncoder(dictionary, input, patch, blockSize: (BlockSize)blockSize,
                chunkSize: 2 * blockSize) { Matcher = MatchAlgorithm.Exhaustive };
            Assert.Equal(IndexPreference.Speed, encoder.IndexPreference);
            Assert.Equal(VCDiffResult.SUCCESS, encoder.Encode());
            Assert.True(patch.Length < target.Length);
            Verify(source, target, patch);

            patch.SetLength(0);
            encoder.IndexPreference = IndexPreference.Memory;
            Assert.Equal(VCDiffResult.SUCCESS, await encoder.EncodeAsync());
            Assert.True(patch.Length < target.Length);
            Verify(source, target, patch);
        }

        [Theory]
        [InlineData(BlockSize.Bytes8)]
        [InlineData(BlockSize.Bytes16)]
        [InlineData(BlockSize.Bytes32)]
        public void MinimumCopyLengthDefaultsToBlockSize(BlockSize blockSize)
        {
            byte[] source = new byte[64];
            new Random(23).NextBytes(source);
            byte[] target = source.AsSpan((int)blockSize, (int)blockSize).ToArray();
            long Size(int minimum = 0)
            {
                using var dictionary = new MemoryStream(source);
                using var input = new MemoryStream(target);
                using var patch = new MemoryStream();
                using var encoder = new VcEncoder(dictionary, input, patch, blockSize: blockSize, chunkSize: minimum)
                    { Matcher = MatchAlgorithm.Exhaustive };
                Assert.Equal(VCDiffResult.SUCCESS, encoder.Encode());
                Verify(source, target, patch);
                return patch.Length;
            }
            Assert.Equal(Size((int)blockSize), Size());
            Assert.True(Size() < Size((int)blockSize + 1));
        }

        [Theory]
        [InlineData(0, 0, "blockSize")]
        [InlineData(12, 0, "blockSize")]
        [InlineData(48, 0, "blockSize")]
        [InlineData(8, -1, "chunkSize")]
        [InlineData(16, 15, "chunkSize")]
        [InlineData(32, 31, "chunkSize")]
        public void InvalidSizesFailAtConstruction(int block, int minimum, string parameter)
        {
            using var source = new MemoryStream(new byte[64]);
            using var dictionary = new ByteBuffer(new byte[64]);
            using var target = new MemoryStream();
            using var patch = new MemoryStream();
            Assert.Throws<ArgumentOutOfRangeException>(parameter, () => new VcEncoder(source, target, patch,
                blockSize: (BlockSize)block, chunkSize: minimum));
            Assert.Throws<ArgumentOutOfRangeException>(parameter, () => new VcEncoder(dictionary, target, patch,
                blockSize: (BlockSize)block, chunkSize: minimum));
        }

        [Fact]
        public async Task AsyncInterleavedEncodingHandlesShortFinalWindow()
        {
            byte[] source = new byte[(1 << 20) + 3];
            new Random(17).NextBytes(source);
            byte[] target = (byte[])source.Clone();
            target[0] ^= 0xFF;
            using var dictionary = new MemoryStream(source);
            using var input = new MemoryStream(target);
            using var patch = new MemoryStream();
            using var encoder = new VcEncoder(dictionary, input, patch) { Matcher = MatchAlgorithm.Exhaustive };
            Assert.Equal(VCDiffResult.SUCCESS, await encoder.EncodeAsync(true, ChecksumFormat.SDCH));
            Verify(source, target, patch);
        }

        [Fact]
        public void DefaultBoundedSearchAndExplicitExhaustiveSearch()
        {
            const int recordSize = 64;
            byte[] source = new byte[recordSize * 512];
            new Random(31).NextBytes(source);
            for (int i = recordSize; i < source.Length; i += recordSize)
                Array.Copy(source, 0, source, i, 32);
            byte[] target = source.AsSpan(source.Length - recordSize).ToArray();
            byte[] Encode(MatchAlgorithm? matcher = null)
            {
                using var dictionary = new ByteBuffer(source);
                using var input = new MemoryStream(target);
                using var patch = new MemoryStream();
                using var encoder = new VcEncoder(dictionary, input, patch, blockSize: BlockSize.Bytes8);
                Assert.Equal(MatchAlgorithm.Bounded, encoder.Matcher);
                if (matcher.HasValue) encoder.Matcher = matcher.Value;
                Assert.Equal(VCDiffResult.SUCCESS, encoder.Encode());
                Verify(source, target, patch);
                return patch.ToArray();
            }
            var bounded = Encode();
            Assert.Equal(Encode(MatchAlgorithm.Bounded), bounded);
            Assert.True(Encode(MatchAlgorithm.Exhaustive).Length < bounded.Length);
        }

        [Theory]
        [InlineData((MatchAlgorithm)0, 8)]
        [InlineData((MatchAlgorithm)99, 8)]
        public void InvalidMatcherOptionsLeaveOutputEmpty(MatchAlgorithm matcher, int blockSize)
        {
            using var dictionary = new MemoryStream(new byte[64]);
            using var input = new MemoryStream(new byte[32]);
            using var patch = new MemoryStream();
            using var encoder = new VcEncoder(dictionary, input, patch, blockSize: (BlockSize)blockSize) { Matcher = matcher };
            Assert.Throws<ArgumentOutOfRangeException>(() => encoder.Encode());
            Assert.Equal(0, patch.Length);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SpeedBucketsSeparateCandidatesThatShareAMemoryBucket(bool useAsync)
        {
            // The last word hashes to 512 in the low ten bits. Zero and this word
            // share a bucket with 512 heads but split with 1,024 heads.
            byte[] target = { 0x8C, 0x04, 0, 0, 0, 0, 0, 0 };
            byte[] source = new byte[257 * 8];
            Array.Copy(target, 0, source, 256 * 8, 8);
            using var dictionary = new ByteBuffer(source);
            using var input = new MemoryStream(target);
            using var patch = new MemoryStream();
            using var encoder = new VcEncoder(dictionary, input, patch, BlockSize.Bytes8);

            async Task<long> PatchLength(IndexPreference preference)
            {
                patch.SetLength(0);
                encoder.IndexPreference = preference;
                Assert.Equal(VCDiffResult.SUCCESS, useAsync ? await encoder.EncodeAsync() : encoder.Encode());
                Verify(source, target, patch);
                return patch.Length;
            }

            // In memory mode the 256 zero blocks exhaust the bounded search before the exact match.
            var memory = await PatchLength(IndexPreference.Memory);
            var speed = await PatchLength(IndexPreference.Speed);
            Assert.True(speed < memory);
        }

        [Fact]
        public async Task InvalidIndexPreferenceFailsBeforeWritingTheHeader()
        {
            using var dictionary = new ByteBuffer(new byte[32]);
            using var target = new MemoryStream(new byte[32]);
            using var patch = new MemoryStream();
            using var encoder = new VcEncoder(dictionary, target, patch, BlockSize.Bytes8)
                { IndexPreference = (IndexPreference)99 };
            Assert.Throws<ArgumentOutOfRangeException>(nameof(encoder.IndexPreference), () => encoder.Encode());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => encoder.EncodeAsync());
            Assert.Equal(0, patch.Length);
        }

        private static void Verify(byte[] source, byte[] target, MemoryStream patch)
        {
            patch.Position = 0;
            using var dictionary = new MemoryStream(source);
            using var output = new MemoryStream();
            using var decoder = new VcDecoder(dictionary, patch, output);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long written));
            Assert.Equal(target.Length, written);
            Assert.True(target.AsSpan().SequenceEqual(output.ToArray()));
        }
    }
}
