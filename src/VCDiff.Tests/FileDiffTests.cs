using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using VCDiff.Decoders;
using VCDiff.Encoders;
using VCDiff.Includes;
using Xunit;

namespace VCDiff.Tests
{
    public class FileDiffTests
    {
        [Fact]
        public void NoChecksumNoInterleaved_Test()
        {
            using var srcStream = File.OpenRead("a.test");
            using var targetStream = File.OpenRead("b.test");
            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            VCCoder coder = new VCCoder(srcStream, targetStream, deltaStream);
            VCDiffResult result = coder.Encode(); //encodes with no checksum and not interleaved
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            VCDecoder decoder = new VCDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Initialize());
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));

            Assert.NotEqual(0, bytesWritten);
        }

        [Fact]
        public void Checksum_Test()
        {
            using var srcStream = File.OpenRead("a.test");
            using var targetStream = File.OpenRead("b.test");
            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            VCCoder coder = new VCCoder(srcStream, targetStream, deltaStream);
            VCDiffResult result = coder.Encode(checksum: true); //encodes with no checksum and not interleaved
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            VCDecoder decoder = new VCDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Initialize());
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));
        }

        [Fact]
        public void ChecksumHash_Test()
        {
            using var srcStream = File.OpenRead("a.test");
            using var targetStream = File.OpenRead("b.test");
            using var md5 = MD5.Create();
            var originalHash = md5.ComputeHash(targetStream);
            targetStream.Position = 0;

            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            VCCoder coder = new VCCoder(srcStream, targetStream, deltaStream);
            VCDiffResult result = coder.Encode(checksum: true); //encodes with no checksum and not interleaved
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            VCDecoder decoder = new VCDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Initialize());
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));
            outputStream.Position = 0;
            var outputHash = md5.ComputeHash(outputStream);
            Assert.Equal(originalHash, outputHash);
        }

        [Fact]
        public void Interleaved_Test()
        {
            using var srcStream = File.OpenRead("a.test");
            using var targetStream = File.OpenRead("b.test");
            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            VCCoder coder = new VCCoder(srcStream, targetStream, deltaStream);
            VCDiffResult result = coder.Encode(interleaved: true); //encodes with no checksum and not interleaved
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            VCDecoder decoder = new VCDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Initialize());

            long bytesWritten = 0;

            while (bytesWritten < targetStream.Length)
            {
                Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long chunk));
                bytesWritten += chunk;
            }
        }
    }
}
