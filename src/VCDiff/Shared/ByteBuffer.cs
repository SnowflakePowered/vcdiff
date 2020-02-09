using System;
using System.Buffers;
using System.IO;

namespace VCDiff.Shared
{
    internal class ByteBuffer : IByteBuffer
    {
        private readonly Memory<byte> bytes;
        private MemoryHandle byteHandle;
        private readonly unsafe void* bytePtr;
        private readonly int length;
        private int offset;

        private readonly MemoryStream? copyStream;
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
                this.bytePtr = this.byteHandle.Pointer;
            }

            length = this.bytes.Length;
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
                this.bytePtr = this.byteHandle.Pointer;
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
                this.bytePtr = this.byteHandle.Pointer;
            }
            length = this.bytes.Length;
        }

        public bool CanRead => offset < length;

        public long Position
        {
            get => offset;
            set
            {
                if (value > bytes.Length || value < 0) return;
                offset = (int)value;
            }
        }

        public long Length => length;

        public byte PeekByte()
        {
            if (offset >= length) throw new Exception("Trying to read past End of Buffer");
            unsafe
            {
                return *((byte*)this.bytePtr + offset);
            }
        }

        public Memory<byte> PeekBytes(int len)
        {
            int sliceLen = offset + len > bytes.Length ? bytes.Length - offset : len;
            return bytes.Slice(offset, sliceLen);
        }

        public byte ReadByte()
        {
            if (offset >= length) throw new Exception("Trying to read past End of Buffer");
            unsafe
            {
                return *((byte*)this.bytePtr + offset++);
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