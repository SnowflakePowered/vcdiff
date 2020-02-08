using System;
using System.IO;

namespace VCDiff.Shared
{
    //Wrapper Class for any stream that supports Position
    //and Length to make reading bytes easier
    //also has a helper function for reading all the bytes in at once
    internal class ByteStreamReader : IByteBuffer
    {
        private readonly MemoryStream buffer;
        private int lastLenRead;

        public ByteStreamReader(Stream stream)
        {
            buffer = new MemoryStream();
            stream.CopyTo(buffer);
            buffer.Seek(0, SeekOrigin.Begin);
        }

        public long Position
        {
            get => buffer.Position;
            set
            {
                if (buffer.CanRead && value >= 0)
                {
                    buffer.Seek(value, SeekOrigin.Begin);
                }
            }
        }

        public long Length => buffer.CanRead ? buffer.Length : 0;

        public bool CanRead => buffer.CanRead && buffer.Position < buffer.Length;

        public ReadOnlyMemory<byte> PeekBytes(int len)
        {
            long oldPos = buffer.Position;
            return buffer.GetBuffer().AsMemory((int)oldPos, (int)Math.Min(len, buffer.Length - oldPos));
        }

        public byte ReadByte()
        {
            if (!CanRead) throw new Exception("Trying to read past end of buffer");
            lastLenRead = buffer.ReadByte();
            if (lastLenRead > -1)
            {
                return (byte) lastLenRead;
            }
            return 0;
        }

        public ReadOnlyMemory<byte> ReadBytes(int len)
        {
            var bytes = this.PeekBytes(len);
            this.buffer.Seek(len, SeekOrigin.Current);
            return bytes;
        }

        public byte PeekByte()
        {
            if (!CanRead) throw new Exception("Trying to read past end of buffer");
            return buffer.GetBuffer()[buffer.Position];
        }

        //increases the offset by 1
        public void Next()
        {
            buffer.Seek(1, SeekOrigin.Current);
        }

        public void Dispose()
        {
            buffer.Dispose();
        }
    }
}