using System;

namespace VCDiff.Shared
{
    internal class ByteBuffer : IByteBuffer
    {
        private readonly ReadOnlyMemory<byte> bytes;
        private readonly int length;
        private int offset;

        /// <summary>
        /// Basically a simple wrapper for byte[] arrays
        /// for easier reading and parsing
        /// </summary>
        /// <param name="bytes"></param>
        public ByteBuffer(byte[] bytes)
        {
            offset = 0;
            this.bytes = bytes != null ? new ReadOnlyMemory<byte>(bytes) : Memory<byte>.Empty;
            length = this.bytes.Length;
        }

        public ByteBuffer(ReadOnlyMemory<byte> bytes)
        {
            offset = 0;
            this.bytes = bytes;
            length = this.bytes.Length;
        }

        public bool CanRead
        {
            get
            {
                return offset < length;
            }
        }

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
            return bytes.Span[offset];
        }

        public ReadOnlyMemory<byte> PeekBytes(int len)
        {
            int sliceLen = offset + len > bytes.Length ? bytes.Length - offset : len;
            return bytes.Slice(offset, sliceLen);
        }

        public byte ReadByte()
        {
            if (offset >= length) throw new Exception("Trying to read past End of Buffer");
            return bytes.Span[offset++];
        }

        public ReadOnlyMemory<byte> ReadBytes(int len)
        {
            var slice = PeekBytes(len);
            offset += len;
            return slice;
        }

        public void Next()
        {
            offset++;
        }

        public void Skip(int len)
        {
            offset += len;
        }

        public void Dispose()
        {
        }
    }
}