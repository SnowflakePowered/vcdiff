using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VCDiff.Shared
{
    public class ByteBuffer : IByteBuffer, IDisposable
    {
        byte[] bytes;
        int length;
        long offset;

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
                offset = value;
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

        public byte[] PeekBytes(int len)
        {
            int end = (int)offset + len > bytes.Length ? bytes.Length : (int)offset + len;
            int realLen = (int)offset + len > bytes.Length ? bytes.Length - (int)offset : len;

            byte[] rbuff = new byte[realLen];
            int cc = 0;
            for (long i = offset; i < end; i++)
            {
                rbuff[cc] = bytes[i];
                cc++;
            }
            return rbuff;
        }

        public byte ReadByte()
        {
            if (offset >= length) throw new Exception("Trying to read past End of Buffer");
            return this.bytes[offset++];
        }

        public byte[] ReadBytes(int len)
        {
            int end = (int)offset + len > bytes.Length ? bytes.Length : (int)offset + len;
            int realLen = (int)offset + len > bytes.Length ? bytes.Length - (int)offset : len;

            byte[] rbuff = new byte[realLen];
            int cc = 0;
            for (long i = offset; i < end; i++)
            {
                rbuff[cc] = bytes[i];
                cc++;
            }
            offset += len;
            return rbuff;
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
