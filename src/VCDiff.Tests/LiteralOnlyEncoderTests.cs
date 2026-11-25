using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VCDiff.Decoders;
using VCDiff.Encoders;
using VCDiff.Includes;
using VCDiff.Shared;
using Xunit;

namespace VCDiff.Tests
{
    public class LiteralOnlyEncoderTests
    {
        [Theory]
        [InlineData(false, 31)]
        [InlineData(true, 31)]
        [InlineData(false, 32)]
        [InlineData(true, 32)]
        public async Task ExactDictionaryMatchBecomesCopyOnlyAtMinimumLength(bool useAsync, int length)
        {
            var source = new byte[4096];
            new Random(17).NextBytes(source);
            var target = source.AsSpan(64, length).ToArray();
            using var dictionary = new ByteBuffer(source);
            using var input = new MemoryStream(target);
            using var patch = new MemoryStream();
            using var encoder = new VcEncoder(dictionary, input, patch, BlockSize.Bytes8, chunkSize: 32);

            Assert.Equal(VCDiffResult.SUCCESS, useAsync ? await encoder.EncodeAsync() : encoder.Encode());
            Assert.Equal(target, Decode(source, patch));

            // With checksums disabled, replacing the dictionary reveals which bytes came from COPY.
            var replacement = new byte[source.Length];
            Assert.Equal(length < 32 ? target : new byte[length], Decode(replacement, patch));
        }

        private sealed class ProgressLog : List<float>, IProgress<float>
        {
            public void Report(float value) => Add(value);
        }

        private sealed class ShortReadStream : MemoryStream
        {
            public ShortReadStream(byte[] data) : base(data, writable: false) { }

            public override int Read(byte[] buffer, int offset, int count)
                => base.Read(buffer, offset, Math.Min(count, 7));

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
                => Task.FromResult(Read(buffer, offset, count));
        }

        [Theory]
        [InlineData(false, false, ChecksumFormat.Xdelta3)]
        [InlineData(true, false, ChecksumFormat.Xdelta3)]
        [InlineData(false, true, ChecksumFormat.SDCH)]
        [InlineData(true, true, ChecksumFormat.SDCH)]
        public async Task ShortLiteralReadsProduceCompleteChecksummedWindows(bool useAsync, bool interleaved, ChecksumFormat checksum)
        {
            byte[] target = new byte[18];
            new Random(18).NextBytes(target);
            using var dictionary = new ByteBuffer(target);
            using var input = new ShortReadStream(target);
            using var patch = new MemoryStream();
            using var encoder = new VcEncoder(dictionary, input, patch, BlockSize.Bytes16, chunkSize: 32);
            var progress = new ProgressLog();

            var result = useAsync
                ? await encoder.EncodeAsync(interleaved, checksum, progress)
                : encoder.Encode(interleaved, checksum, progress);

            Assert.Equal(VCDiffResult.SUCCESS, result);
            Assert.Equal(new[] { 7f / 18, 14f / 18, 1f }, progress.ToArray());
            Assert.Equal(target, Decode(new byte[target.Length], patch));
        }

        private static byte[] Decode(byte[] source, MemoryStream patch)
        {
            patch.Position = 0;
            using var dictionary = new MemoryStream(source);
            using var output = new MemoryStream();
            using var decoder = new VcDecoder(dictionary, patch, output);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long written));
            Assert.Equal(output.Length, written);
            return output.ToArray();
        }
    }
}
