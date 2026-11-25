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
    public class DecoderCopyStreamTests
    {
        private sealed class ShortReadStream : MemoryStream
        {
            private readonly long declaredLength;

            public ShortReadStream(byte[] data, long declaredLength) : base(data, writable: false)
                => this.declaredLength = declaredLength;

            public override long Length => declaredLength;
            public int ReadCalls { get; private set; }

            public override int Read(byte[] buffer, int offset, int count)
            {
                ReadCalls++;
                return base.Read(buffer, offset, Math.Min(count, 113));
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task LongDictionaryCopyRetriesShortReads(bool interleaved, bool useAsync)
        {
            // A single COPY is larger than the stream reader’s 8 KiB cache.
            byte[] source = new byte[300_000];
            new Random(7).NextBytes(source);
            byte[] target = source.AsSpan(123, 280_000).ToArray();
            using var patch = new MemoryStream();
            using (var original = new ByteBuffer(source))
            using (var input = new MemoryStream(target))
            using (var encoder = new VcEncoder(original, input, patch, BlockSize.Bytes16))
                Assert.Equal(VCDiffResult.SUCCESS, encoder.Encode(interleaved));

            using var dictionary = new ShortReadStream(source, source.Length);
            patch.Position = 0;
            using var output = new MemoryStream();
            using var decoder = new VcDecoder(dictionary, patch, output);

            long written;
            VCDiffResult result;
            if (useAsync)
                (result, written) = await decoder.DecodeAsync();
            else
                result = decoder.Decode(out written);

            Assert.Equal(VCDiffResult.SUCCESS, result);
            Assert.Equal(target.Length, written);
            Assert.True(dictionary.ReadCalls > 1);
            Assert.True(target.AsSpan().SequenceEqual(output.ToArray()));
        }

        [Theory]
        [InlineData(3)]  // The source ends exactly where the copy destination begins.
        [InlineData(26)] // The copy repeats and finally uses a partial prefix.
        public void TargetCopyExpandsAnExistingPrefix(int copySize)
        {
            byte[] patch = { 0xD6, 0xC3, 0xC4, 0, 0, 0, 12, (byte)(3 + copySize),
                0, 3, 3, 1, (byte)'a', (byte)'b', (byte)'c', 4, 19, (byte)copySize, 0 };
            using var dictionary = new MemoryStream();
            using var input = new MemoryStream(patch);
            using var output = new MemoryStream();
            using var decoder = new VcDecoder(dictionary, input, output);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long written));
            byte[] expected = new byte[3 + copySize];
            for (int i = 0; i < expected.Length; i++)
                expected[i] = (byte)('a' + i % 3);
            Assert.Equal(expected.Length, written);
            Assert.Equal(expected, output.ToArray());
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task PrematureDictionaryEofKeepsOnlyCompleteWindows(bool interleaved, bool useAsync)
        {
            // First window ADDs 'A'; second window COPYs all four bytes of source segment [2,6).
            byte[] patch = interleaved
                ? new byte[] { 0xD6, 0xC3, 0xC4, (byte)'S', 0,
                    0, 7, 1, 0, 0, 2, 0, 2, (byte)'A',
                    1, 4, 2, 7, 4, 0, 0, 2, 0, 20, 0 }
                : new byte[] { 0xD6, 0xC3, 0xC4, 0, 0,
                    0, 7, 1, 0, 1, 1, 0, (byte)'A', 2,
                    1, 4, 2, 7, 4, 0, 0, 1, 1, 20, 0 };
            using var dictionary = new ShortReadStream(new byte[5], declaredLength: 6);
            using var input = new MemoryStream(patch);
            using var output = new MemoryStream();
            using var decoder = new VcDecoder(dictionary, input, output);

            long written;
            VCDiffResult result;
            if (useAsync)
                (result, written) = await decoder.DecodeAsync();
            else
                result = decoder.Decode(out written);

            Assert.Equal(VCDiffResult.ERROR, result);
            Assert.Equal(1L, written);
            Assert.Equal(new byte[] { (byte)'A' }, output.ToArray());
        }
    }
}
