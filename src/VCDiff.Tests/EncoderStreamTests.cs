using System;
using System.IO;
using System.Threading.Tasks;
using VCDiff.Encoders;
using VCDiff.Includes;
using VCDiff.Shared;
using Xunit;

namespace VCDiff.Tests
{
    public class EncoderStreamTests
    {
        private sealed class ShortReadStream : MemoryStream
        {
            public ShortReadStream(byte[] data) : base(data) { }

            public override int Read(byte[] buffer, int offset, int count)
                => base.Read(buffer, offset, Math.Min(count, 17));
        }

        [Fact]
        public void ShortDictionaryReadsProduceTheSamePatchAsAnInMemoryDictionary()
        {
            byte[] bytes = new byte[4096];
            new Random(23).NextBytes(bytes);
            using var target = new MemoryStream(bytes);
            using var source = new ShortReadStream(bytes);
            using var dictionary = new ByteBuffer(bytes);
            using var actual = new MemoryStream();
            using var expected = new MemoryStream();
            using var fromStream = new VcEncoder(source, target, actual, BlockSize.Bytes8);
            using var fromBuffer = new VcEncoder(dictionary, target, expected, BlockSize.Bytes8);
            Assert.Equal(VCDiffResult.SUCCESS, fromStream.Encode());
            Assert.Equal(VCDiffResult.SUCCESS, fromBuffer.Encode());
            Assert.Equal(expected.ToArray(), actual.ToArray());
        }

        [Fact]
        public void PrematureDictionaryEofFailsAtConstruction()
        {
            using var source = new MemoryStream(new byte[64]);
            source.Position = 1;
            using var target = new MemoryStream();
            using var patch = new MemoryStream();
            Assert.Throws<EndOfStreamException>(() => new VcEncoder(source, target, patch, BlockSize.Bytes16));
        }

        [Fact]
        public void SyncHeaderWritePropagatesTheStreamException()
        {
            using var source = new ByteBuffer(new byte[16]);
            using var target = new MemoryStream(new byte[16]);
            using var patch = new MemoryStream(Array.Empty<byte>(), writable: false);
            using var encoder = new VcEncoder(source, target, patch, BlockSize.Bytes16);
            Assert.Throws<NotSupportedException>(() => encoder.Encode());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task LargeWindowLimitIsClampedToTheSmallTarget(bool useAsync)
        {
            byte[] bytes = new byte[4096];
            using var source = new ByteBuffer(bytes);
            using var target = new MemoryStream(bytes);
            using var actual = new MemoryStream();
            using var expected = new MemoryStream();
            using var normal = new VcEncoder(source, target, expected, BlockSize.Bytes16);
            using var large = new VcEncoder(source, target, actual, 2048, BlockSize.Bytes16);
            Assert.Equal(VCDiffResult.SUCCESS, normal.Encode());
            Assert.Equal(VCDiffResult.SUCCESS, useAsync ? await large.EncodeAsync() : large.Encode());
            Assert.Equal(expected.ToArray(), actual.ToArray());
        }
    }
}
