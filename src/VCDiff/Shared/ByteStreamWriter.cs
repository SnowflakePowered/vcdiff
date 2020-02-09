using System;
using System.IO;

namespace VCDiff.Shared
{
    internal class ByteStreamWriter : IDisposable
    {
        private Stream buffer;

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
        }

        public long Position => buffer.Position;

        public void Write(byte b)
        {
            buffer.WriteByte(b);
        }

        public void Write(byte[] b)
        {
            buffer.Write(b, 0, b.Length);
        }

        public void Write(ReadOnlySpan<byte> b)
        {
            buffer.Write(b);
        }

        public void Dispose()
        {
            buffer.Dispose();
        }
    }
}