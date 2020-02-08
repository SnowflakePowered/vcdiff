using System;
using System.IO;

namespace VCDiff.Shared
{
    public class ByteStreamWriter : IDisposable
    {
        private Stream buffer;

        private bool isLittle;

        /// <summary>
        /// Wrapper class for writing to streams
        /// with a little bit easier functionality
        /// also detects whether it is little endian
        /// to encode into BE properly
        /// </summary>
        /// <param name="s"></param>
        public ByteStreamWriter(Stream s)
        {
            buffer = s;
            isLittle = BitConverter.IsLittleEndian;
        }

        public byte[] ToArray()
        {
            if (buffer.GetType().Equals(typeof(MemoryStream)))
            {
                MemoryStream buff = (MemoryStream)buffer;
                return buff.ToArray();
            }

            return new byte[0];
        }

        public long Position
        {
            get
            {
                return buffer.Position;
            }
        }

        public void Write(byte b)
        {
            this.buffer.WriteByte(b);
        }

        public void Write(byte[] b)
        {
            this.buffer.Write(b, 0, b.Length);
        }

        public void Write(ReadOnlyMemory<byte> b)
        {
            this.buffer.Write(b.Span);
        }

        public void Dispose()
        {
            this.buffer.Dispose();
        }
    }
}