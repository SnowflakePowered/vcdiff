using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VCDiff.Encoders;
using VCDiff.Includes;
using VCDiff.Shared;
using Xunit;

namespace VCDiff.Tests
{
    public class EncoderBehaviorTests
    {
        private sealed class ReportedLengthStream : MemoryStream
        {
            public ReportedLengthStream(byte[] bytes, long length) : base(bytes) => Length = length;

            public override long Length { get; }
        }

        private sealed class ProgressLog : List<float>, IProgress<float>
        {
            public void Report(float value) => Add(value);
        }

        [Theory]
        [InlineData(0, 16, true)]
        [InlineData(16, 0, true)]
        [InlineData(16, 16, false)]
        public async Task UnavailableInputReturnsErrorBeforeWritingOrReporting(int sourceBytes, int targetBytes, bool readable)
        {
            using var source = new ByteBuffer(new byte[sourceBytes]);
            using var target = new MemoryStream(new byte[targetBytes]);
            if (!readable)
                target.Dispose();
            using var patch = new MemoryStream();
            patch.WriteByte(0xA5);
            var progress = new ProgressLog();
            using var encoder = new VcEncoder(source, target, patch, BlockSize.Bytes16);

            Assert.Equal(VCDiffResult.ERROR, encoder.Encode(progress: progress));
            Assert.Equal(VCDiffResult.ERROR, await encoder.EncodeAsync(progress: progress));
            Assert.Empty(progress);
            Assert.Equal(new byte[] { 0xA5 }, patch.ToArray());
        }

        [Fact]
        public void OversizedDictionaryIsRejectedBeforeReading()
        {
            using var source = new ReportedLengthStream(Array.Empty<byte>(), (long)int.MaxValue + 1);
            using var target = new MemoryStream(new byte[16]);
            using var patch = new MemoryStream();

            Assert.Throws<ArgumentOutOfRangeException>("source",
                () => new VcEncoder(source, target, patch, BlockSize.Bytes16));
            Assert.Equal(0, source.Position);
            Assert.Equal(0, patch.Length);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task TruncatedTargetReportsOnlyTheCompletedWindow(bool useAsync)
        {
            using var source = new ByteBuffer(new byte[32]);
            using var target = new ReportedLengthStream(new byte[16], 32);
            using var patch = new MemoryStream();
            using var encoder = new VcEncoder(source, target, patch, BlockSize.Bytes16);
            var progress = new ProgressLog();

            if (useAsync)
                await Assert.ThrowsAsync<EndOfStreamException>(() => encoder.EncodeAsync(progress: progress));
            else
                Assert.Throws<EndOfStreamException>(() => encoder.Encode(progress: progress));

            Assert.Equal(new[] { 0.5f }, progress.ToArray());
        }

        [Fact]
        public async Task RepeatedEncodingRewindsTheTargetAndReportsEachWindow()
        {
            const int windowSize = 1024 * 1024;
            byte[] bytes = new byte[windowSize + 16];
            using var source = new ByteBuffer(new byte[16]);
            using var target = new MemoryStream(bytes);
            using var patch = new MemoryStream();
            using var encoder = new VcEncoder(source, target, patch, BlockSize.Bytes16);
            var progress = new ProgressLog();
            float[] expectedProgress = { (float)windowSize / bytes.Length, 1f };
            target.Position = 16;

            Assert.Equal(VCDiffResult.SUCCESS, encoder.Encode(progress: progress));
            Assert.Equal(expectedProgress, progress.ToArray());

            byte[] expectedPatch = patch.ToArray();
            patch.SetLength(0);
            progress.Clear();
            Assert.Equal(VCDiffResult.SUCCESS, await encoder.EncodeAsync(progress: progress));
            Assert.Equal(expectedProgress, progress.ToArray());
            Assert.Equal(expectedPatch, patch.ToArray());
        }
    }
}
