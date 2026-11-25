using System;
using System.IO;
using System.Threading.Tasks;
using VCDiff.Shared;
using Xunit;

namespace VCDiff.Tests
{
    public class ByteReaderTests
    {
        [Fact]
        public void ReadsStayWithinTheSuppliedMemorySlice()
        {
            byte[] storage = { 99, 10, 20, 30, 88 };
            using var buffer = new ByteBuffer(storage.AsMemory(1, 3));

            Assert.Equal(3L, buffer.Length);
            Assert.Equal((byte)10, buffer.PeekByte());
            Assert.Equal(0L, buffer.Position);
            buffer.Next();
            Assert.Equal(new byte[] { 20, 30 }, buffer.PeekBytes(10).ToArray());

            byte[] destination = { 0, 0, 77 };
            Assert.Equal(new byte[] { 20, 30 }, buffer.ReadBytesToSpan(destination).ToArray());
            Assert.Equal(new byte[] { 20, 30, 77 }, destination);
            Assert.Equal(buffer.Length, buffer.Position);
            Assert.False(buffer.CanRead);
            Assert.True(buffer.ReadBytes(10).IsEmpty);
        }

        [Fact]
        public void SpanReadsBorrowStorageAndMemoryReadsReturnACopy()
        {
            Span<byte> storage = stackalloc byte[] { 1, 2, 3 };
            using var buffer = new ByteBuffer(storage);
            var view = buffer.ReadBytesAsSpan(2);
            buffer.Position = 0;
            var copy = buffer.ReadBytes(2);

            storage[0] = 42;
            Assert.Equal((byte)42, view[0]);
            Assert.Equal((byte)1, copy.Span[0]);
            Assert.Equal((byte)3, buffer.ReadByte());
            Assert.False(buffer.CanRead);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(8193)]
        public void StreamReadsReturnOnlyAvailableBytes(int requested)
        {
            using var source = new MemoryStream(new byte[] { 1, 2, 3, 4 });
            using var reader = new ByteStreamReader(source);
            reader.Position = 1;
            Assert.Equal((byte)2, reader.PeekByte());
            Assert.Equal(1L, reader.Position);
            reader.Next();
            Assert.Equal(new byte[] { 3, 4 }, reader.ReadBytes(requested).ToArray());
            Assert.False(reader.CanRead);
            Assert.True(reader.ReadBytesAsSpan(requested).IsEmpty);
            Assert.True(reader.ReadBytes(requested).IsEmpty);
        }

        [Fact]
        public async Task SyncAndAsyncStreamReadsRespectDestinationSlices()
        {
            using var source = new MemoryStream(new byte[] { 11, 22, 33 });
            byte[] destination = { 99, 99, 99, 99 };
            using (var reader = new ByteStreamReader(source))
            {
                Assert.Equal(2, reader.ReadBytesIntoBuf(destination.AsSpan(1, 2)));
                Assert.Equal(new byte[] { 99, 11, 22, 99 }, destination);
                Assert.Equal(1, await reader.ReadBytesIntoBufAsync(destination.AsMemory(1, 2)));
                Assert.Equal(new byte[] { 99, 33, 22, 99 }, destination);
                Assert.Equal(0, await reader.ReadBytesIntoBufAsync(destination));
            }
            source.Position = 0;
            Assert.Equal(11, source.ReadByte());
        }
    }
}
