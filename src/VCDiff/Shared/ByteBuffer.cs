using System;

namespace VCDiff.Shared
{
    internal class ByteBuffer : IByteBuffer, IDisposable
    {
        private byte[] bytes;
        private int length;
        private int offset;

        /// <summary>
        /// Basically a simple wrapper for byte[] arrays
        /// for easier reading and parsing
        /// </summary>
        /// <param name="bytes"></param>
        public ByteBuffer(byte[] bytes)
        {
            offset = 0;
            this.bytes = bytes;
            if (bytes != null)
            {
                this.length = bytes.Length;
            }
            else
            {
                this.length = 0;
            }
        }

        public ByteBuffer(ReadOnlyMemory<byte> bytes)
        {
            offset = 0;
            this.bytes = bytes.ToArray();
            if (this.bytes != null)
            {
                this.length = this.bytes.Length;
            }
            else
            {
                this.length = 0;
            }
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
            get
            {
                return offset;
            }
            set
            {
                if (value > bytes.Length || value < 0) return;
                offset = (int)value;
            }
        }

        public void BufferAll()
        {
            //not implemented in this one
            //since it already contains the full buffered data
        }

        public long Length
        {
            get
            {
                return length;
            }
        }

        public byte PeekByte()
        {
            if (offset >= length) throw new Exception("Trying to read past End of Buffer");
            return this.bytes[offset];
        }

        public ReadOnlyMemory<byte> PeekBytes(int len)
        {
            int sliceLen = offset + len > bytes.Length ? bytes.Length - (int)offset : len;
            return this.bytes.AsMemory().Slice((int)offset, sliceLen);
        }

        public byte ReadByte()
        {
            if (offset >= length) throw new Exception("Trying to read past End of Buffer");
            return this.bytes[offset++];
        }

        public ReadOnlyMemory<byte> ReadBytes(int len)
        {
            var slice = this.PeekBytes(len);
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
            bytes = null;
        }
    }
}