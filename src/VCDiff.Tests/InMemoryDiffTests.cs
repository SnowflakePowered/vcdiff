using System;
using System.IO;
using System.Text;
using VCDiff.Decoders;
using VCDiff.Encoders;
using VCDiff.Includes;
using Xunit;

namespace VCDiff.Tests
{
    public class InMemoryDiffTests
    {
        private static readonly ReadOnlyMemory<byte> ADiffData = Encoding.UTF8.GetBytes("Hello World");
        private static readonly ReadOnlyMemory<byte> BDiffData = Encoding.UTF8.GetBytes("Goodbye World");

        [Fact]
        public void NoChecksumNoInterleaved_Test()
        {
            using var srcStream = new MemoryStream(ADiffData.ToArray());
            using var targetStream = new MemoryStream(BDiffData.ToArray());
            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream);
            VCDiffResult result = coder.Encode(); //encodes with no checksum and not interleaved
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            VcDecoder decoder = new VcDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));

            Assert.Equal("Goodbye World", Encoding.UTF8.GetString(outputStream.ToArray()));
            Assert.NotEqual(0, bytesWritten);
        }

        [Fact]
        public void Checksum_Test()
        {
            using var srcStream = new MemoryStream(ADiffData.ToArray());
            using var targetStream = new MemoryStream(BDiffData.ToArray());
            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream);
            VCDiffResult result = coder.Encode(checksum: true); //encodes with no checksum and not interleaved
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            VcDecoder decoder = new VcDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));

            Assert.Equal("Goodbye World", Encoding.UTF8.GetString(outputStream.ToArray()));
        }

        [Fact]
        public void Interleaved_Test()
        {
            using var srcStream = new MemoryStream(ADiffData.ToArray());
            using var targetStream = new MemoryStream(BDiffData.ToArray());
            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream);
            VCDiffResult result = coder.Encode(interleaved: true); //encodes with no checksum and not interleaved
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            VcDecoder decoder = new VcDecoder(srcStream, deltaStream, outputStream);

            long bytesWritten = 0;

            while (bytesWritten < BDiffData.Length)
            {
                Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long chunk));
                bytesWritten += chunk;
            }
            Assert.Equal("Goodbye World", Encoding.UTF8.GetString(outputStream.ToArray()));
        }
    }
}
