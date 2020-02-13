using System;
using System.Buffers;
using System.IO;
using System.Threading.Tasks;

namespace VCDiff.Shared
{
    internal class ByteBuffer : IByteBuffer
    {
        private Memory<byte> bytes;
        private MemoryHandle byteHandle;
        private unsafe byte* bytePtr;
        private int length;
        private int offset;

        private MemoryStream? copyStream;

        private ByteBuffer()
        {

        }

        public ByteBuffer(Stream copyStream)
        {
            this.copyStream = new MemoryStream();
            copyStream.CopyTo(this.copyStream);
            this.copyStream.Seek(0, SeekOrigin.Begin);
            offset = 0;
            this.bytes = new Memory<byte>(this.copyStream.GetBuffer(),0, (int)copyStream.Length);
            this.byteHandle = this.bytes.Pin();
            unsafe
            {
                this.bytePtr = (byte *)this.byteHandle.Pointer;
            }

            length = this.bytes.Length;
        }

        public static async Task<ByteBuffer> CreateBufferAsync(Stream copyStream)
        {
            var buffer = new ByteBuffer();
            buffer.copyStream = new MemoryStream();
            await copyStream.CopyToAsync(buffer.copyStream);
            buffer.copyStream.Seek(0, SeekOrigin.Begin);
            buffer.offset = 0;
            buffer.bytes = new Memory<byte>(buffer.copyStream.GetBuffer(), 0, (int)copyStream.Length);
            buffer.byteHandle = buffer.bytes.Pin();
            unsafe
            {
                buffer.bytePtr = (byte *)buffer.byteHandle.Pointer;
            }

            buffer.length = buffer.bytes.Length;

            return buffer;
        }

        public unsafe byte* DangerousGetBytePointerAndIncreaseOffsetAfter(int read)
        {
            byte* ptr = bytePtr + offset;
            offset += read;
            return ptr;
        }
        /// <summary>
        /// Basically a simple wrapper for byte[] arrays
        /// for easier reading and parsing
        /// </summary>
        /// <param name="bytes"></param>
        public ByteBuffer(byte[] bytes)
        {
            offset = 0;
            this.bytes = bytes != null ? new Memory<byte>(bytes) : Memory<byte>.Empty;
            this.byteHandle = this.bytes.Pin();
            unsafe
            {
                this.bytePtr = (byte *)this.byteHandle.Pointer;
            }

            length = this.bytes.Length;
        }

        public ByteBuffer(Memory<byte> bytes)
        {
            offset = 0;
            this.bytes = bytes;
            this.byteHandle = bytes.Pin();
            unsafe
            {
                this.bytePtr = (byte *)this.byteHandle.Pointer;
            }
            length = this.bytes.Length;
        }

        public bool CanRead => offset < length;

        public long Position
        {
            get => offset;
            set
            {
                if (value > length || value < 0) return;
                offset = (int)value;
            }
        }

        public long Length => length;

        public byte PeekByte()
        {
            unsafe
            {
                return *((byte*)this.bytePtr + offset);
            }
        }

        public Memory<byte> PeekBytes(int len)
        {
            int sliceLen = offset + len > this.length ? this.length - offset : len;
            return bytes.Slice(offset, sliceLen);
        }

        public byte ReadByte()
        {
            unsafe
            {
                return this.bytePtr[offset++];
            }
        }

        public Memory<byte> ReadBytes(int len)
        {
            var slice = PeekBytes(len);
            offset += len;
            return slice;
        }

        public void Next()
        {
            offset++;
        }

        public void Dispose()
        {
            this.byteHandle.Dispose();
            this.copyStream?.Dispose();
        }
    }
}