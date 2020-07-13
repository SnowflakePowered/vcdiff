using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using VCDiff.Decoders;
using VCDiff.Encoders;
using VCDiff.Includes;
using VCDiff.Shared;
using Xunit;

namespace VCDiff.Tests
{
    public class FileDiffTests
    {
        [Fact]
        public void NoChecksumNoInterleaved_Test()
        {
            using var srcStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}a.test");
            using var targetStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}b.test");
            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            using VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream);
            VCDiffResult result = coder.Encode(); //encodes with no checksum and not interleaved
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            using VcDecoder decoder = new VcDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));
            Assert.NotEqual(0, bytesWritten);
        }

        [Fact]
        public void NoChecksumEmptyHash_Test()
        {
            using var srcStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}empty.test");
            using var targetStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}b.test");
            using var md5 = MD5.Create();
            var originalHash = md5.ComputeHash(targetStream);
            targetStream.Position = 0;

            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            using VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream);
            VCDiffResult result = coder.Encode(); 
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            using VcDecoder decoder = new VcDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));
            outputStream.Position = 0;
            var outputHash = md5.ComputeHash(outputStream);
            Assert.Equal(originalHash, outputHash);
        }

        [Fact]
        public void NoChecksumGoogle_Test()
        {
            using var srcStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}size-overflow-32");
            using var targetStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}size-overflow-64");
            using var md5 = MD5.Create();
            var originalHash = md5.ComputeHash(targetStream);
            targetStream.Position = 0;

            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            using VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream);
            VCDiffResult result = coder.Encode();
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            using VcDecoder decoder = new VcDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));
            outputStream.Position = 0;
            var outputHash = md5.ComputeHash(outputStream);
            Assert.Equal(originalHash, outputHash);
        }

        [Fact]
        public void NoChecksumGoogleTo_Test()
        {
            using var srcStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}size-overflow-64");
            using var targetStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}size-overflow-32");
            using var md5 = MD5.Create();
            var originalHash = md5.ComputeHash(targetStream);
            targetStream.Position = 0;

            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            using VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream);
            VCDiffResult result = coder.Encode();
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            using VcDecoder decoder = new VcDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));
            outputStream.Position = 0;
            var outputHash = md5.ComputeHash(outputStream);
            Assert.Equal(originalHash, outputHash);
        }

        [Fact]
        public void NoChecksumGoogleSame_Test()
        {
            using var srcStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}size-overflow-64");
            using var targetStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}size-overflow-64");
            using var md5 = MD5.Create();
            var originalHash = md5.ComputeHash(targetStream);
            targetStream.Position = 0;

            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            using VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream);
            VCDiffResult result = coder.Encode();
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            using VcDecoder decoder = new VcDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));
            outputStream.Position = 0;
            var outputHash = md5.ComputeHash(outputStream);
            Assert.Equal(originalHash, outputHash);
        }

        [Fact]
        public void NoChecksumEmptyToHash_Test()
        {
            using var srcStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}b.test");
            using var targetStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}empty.test");
            using var md5 = MD5.Create();
            var originalHash = md5.ComputeHash(targetStream);
            targetStream.Position = 0;

            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            using VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream);
            VCDiffResult result = coder.Encode();
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            using VcDecoder decoder = new VcDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));
            outputStream.Position = 0;
            var outputHash = md5.ComputeHash(outputStream);
            Assert.Equal(originalHash, outputHash);
        }

        [Fact]
        public void ChecksumEmptyHash_Test()
        {
            using var srcStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}empty.test");
            using var targetStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}b.test");
            using var md5 = MD5.Create();
            var originalHash = md5.ComputeHash(targetStream);
            targetStream.Position = 0;

            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            using VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream);
            VCDiffResult result = coder.Encode(checksumFormat: ChecksumFormat.SDCH);
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            using VcDecoder decoder = new VcDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));
            outputStream.Position = 0;
            var outputHash = md5.ComputeHash(outputStream);
            Assert.Equal(originalHash, outputHash);
        }



        [Fact]
        public void Checksum_Test()
        {
            using var srcStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}a.test");
            using var targetStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}b.test");
            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            using VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream, blockSize: 32);
            VCDiffResult result = coder.Encode(checksumFormat: ChecksumFormat.SDCH); //encodes with no checksum and not interleaved
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            using VcDecoder decoder = new VcDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));
        }

        [Fact]
        public void ChecksumHash_Test()
        {
            using var srcStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}a.test");
            using var targetStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}b.test");
            using var md5 = MD5.Create();
            var originalHash = md5.ComputeHash(targetStream);
            targetStream.Position = 0;

            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            using VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream);
            VCDiffResult result = coder.Encode(checksumFormat: ChecksumFormat.SDCH); //encodes with no checksum and not interleaved
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            using VcDecoder decoder = new VcDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));
            outputStream.Position = 0;
            var outputHash = md5.ComputeHash(outputStream);
            Assert.Equal(originalHash, outputHash);
        }

        [Fact]
        public void InterleaveFailXdelta3_Test()
        {
            using var srcStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}a.test");
            using var targetStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}b.test");
            using var md5 = MD5.Create();
            var originalHash = md5.ComputeHash(targetStream);
            targetStream.Position = 0;

            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            using VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream);
            Assert.Throws<ArgumentException>(() => coder.Encode(interleaved: true, checksumFormat: ChecksumFormat.Xdelta3)); //encodes with no checksum and not interleaved
        }

        [Fact]
        public void Xdelta3ChecksumHash_Test()
        {
            using var srcStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}a.test");
            using var targetStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}b.test");
            using var md5 = MD5.Create();
            var originalHash = md5.ComputeHash(targetStream);
            targetStream.Position = 0;

            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            using VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream);
            VCDiffResult result = coder.Encode(checksumFormat: ChecksumFormat.Xdelta3); //encodes with no checksum and not interleaved
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            using VcDecoder decoder = new VcDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));
            outputStream.Position = 0;
            deltaStream.Position = 0;
            var outputHash = md5.ComputeHash(outputStream);
            Assert.Equal(originalHash, outputHash);
            //File.WriteAllBytes("patch.xdelta", deltaStream.ToArray());
        }


        [Fact]
        public void Xdelta3ChecksumHash32Block_Test()
        {
            using var srcStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}a.test");
            using var targetStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}b.test");
            using var md5 = MD5.Create();
            var originalHash = md5.ComputeHash(targetStream);
            targetStream.Position = 0;

            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            using VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream, blockSize: 32);
            VCDiffResult result = coder.Encode(checksumFormat: ChecksumFormat.Xdelta3); //encodes with no checksum and not interleaved
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            using VcDecoder decoder = new VcDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));
            outputStream.Position = 0;
            deltaStream.Position = 0;
            var outputHash = md5.ComputeHash(outputStream);
            Assert.Equal(originalHash, outputHash);
        }

        [Fact]
        public void Xdelta3ChecksumHash48Block_Test()
        {
            using var srcStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}a.test");
            using var targetStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}b.test");
            using var md5 = MD5.Create();
            var originalHash = md5.ComputeHash(targetStream);
            targetStream.Position = 0;

            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            using VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream, blockSize: 48);
            VCDiffResult result = coder.Encode(checksumFormat: ChecksumFormat.Xdelta3); //encodes with no checksum and not interleaved
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            using VcDecoder decoder = new VcDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));
            outputStream.Position = 0;
            deltaStream.Position = 0;
            var outputHash = md5.ComputeHash(outputStream);
            Assert.Equal(originalHash, outputHash);
        }

        [Fact]
        public void Xdelta3ChecksumHash32BlockBenchmark_Test()
        {
            using var srcStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}a.test");
            using var targetStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}b.test");
            using var md5 = MD5.Create();
            var originalHash = md5.ComputeHash(targetStream);
            targetStream.Position = 0;

            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            using var hasher = new RollingHash(32);
            
            using VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream, blockSize: 32, rollingHash: hasher);
            VCDiffResult result = coder.Encode(checksumFormat: ChecksumFormat.Xdelta3); //encodes with no checksum and not interleaved
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            using VcDecoder decoder = new VcDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));
            outputStream.Position = 0;
            deltaStream.Position = 0;
            var outputHash = md5.ComputeHash(outputStream);
            Assert.Equal(originalHash, outputHash);
            File.WriteAllBytes("patch.xdelta", deltaStream.ToArray());
        }

        [Fact]
        public async Task AsyncEncode_Test()
        {
            using var srcStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}a.test");
            using var targetStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}b.test");
            using var md5 = MD5.Create();
            var originalHash = md5.ComputeHash(targetStream);
            targetStream.Position = 0;

            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            using VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream);
            VCDiffResult result = await coder.EncodeAsync(checksumFormat: ChecksumFormat.Xdelta3); //encodes with no checksum and not interleaved
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            using VcDecoder decoder = new VcDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));
            outputStream.Position = 0;
            var outputHash = md5.ComputeHash(outputStream);
            Assert.Equal(originalHash, outputHash);
        }

        [Fact]
        public void NoChecksumHash_Test()
        {
            using var srcStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}a.test");
            using var targetStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}b.test");
            using var md5 = MD5.Create();
            var originalHash = md5.ComputeHash(targetStream);
            targetStream.Position = 0;

            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            using VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream);
            VCDiffResult result = coder.Encode(); //encodes with no checksum and not interleaved
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            using VcDecoder decoder = new VcDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));
            outputStream.Position = 0;
            var outputHash = md5.ComputeHash(outputStream);
            Assert.Equal(originalHash, outputHash);
        }

        [Fact]
        public void ChecksumHashSmall_block_Test()
        {
            using var srcStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}a.test");
            using var targetStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}b.test");
            using var md5 = MD5.Create();
            var originalHash = md5.ComputeHash(targetStream);
            targetStream.Position = 0;

            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            using VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream, blockSize: 8);
            VCDiffResult result = coder.Encode(checksumFormat: ChecksumFormat.SDCH); //encodes with no checksum and not interleaved
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            using VcDecoder decoder = new VcDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));
            outputStream.Position = 0;
            var outputHash = md5.ComputeHash(outputStream);
            Assert.Equal(originalHash, outputHash);
        }

        [Fact]
        public void ChecksumHashLarge_block_Test()
        {
            using var srcStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}a.test");
            using var targetStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}b.test");
            using var md5 = MD5.Create();
            var originalHash = md5.ComputeHash(targetStream);
            targetStream.Position = 0;

            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            using VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream, blockSize: 32);
            VCDiffResult result = coder.Encode(checksumFormat: ChecksumFormat.SDCH); //encodes with no checksum and not interleaved
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            using VcDecoder decoder = new VcDecoder(srcStream, deltaStream, outputStream);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));
            outputStream.Position = 0;
            var outputHash = md5.ComputeHash(outputStream);
            Assert.Equal(originalHash, outputHash);
        }


        [Fact]
        public void Interleaved_Test()
        {
            using var srcStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}a.test");
            using var targetStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}b.test");
            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            using var md5 = MD5.Create();
            var originalHash = md5.ComputeHash(targetStream);
            targetStream.Position = 0;

            using VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream);
            VCDiffResult result = coder.Encode(interleaved: true); //encodes with no checksum and not interleaved
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            using VcDecoder decoder = new VcDecoder(srcStream, deltaStream, outputStream);

            long bytesWritten = 0;

            while (bytesWritten < targetStream.Length)
            {
                Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long chunk));
                bytesWritten += chunk;
            }

            outputStream.Position = 0;
            var outputHash = md5.ComputeHash(outputStream);
            Assert.Equal(originalHash, outputHash);
        }

        [Fact]
        public void MaxFileSize_Test()
        {
            using var srcStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}a.test");
            using var targetStream = File.OpenRead($"patches{Path.DirectorySeparatorChar}b.test");
            using var deltaStream = new MemoryStream();
            using var outputStream = new MemoryStream();

            using VcEncoder coder = new VcEncoder(srcStream, targetStream, deltaStream);
            VCDiffResult result = coder.Encode();
            Assert.Equal(VCDiffResult.SUCCESS, result);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            long bytesWritten = 0;

            using VcDecoder decoder = new VcDecoder(srcStream, deltaStream, outputStream, -1);
            ArgumentException ex = Assert.Throws<ArgumentException>(() => decoder.Decode(out bytesWritten));
            Assert.Matches(@"maxWindowSize must be a positive value", ex.Message);

            srcStream.Position = 0;
            targetStream.Position = 0;
            deltaStream.Position = 0;

            using VcDecoder decoder1 = new VcDecoder(srcStream, deltaStream, outputStream, 2);
            InvalidOperationException ex1 = Assert.Throws<InvalidOperationException>(() => decoder1.Decode(out bytesWritten));
            Assert.Matches(@"Length of target window \(\d*\) exceeds limit of 2 bytes", ex1.Message);
            

        }
    }
}
