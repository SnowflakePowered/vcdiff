using System;
using System.Collections.Generic;
using System.IO;

namespace VCDiff.Shared
{
    //Wrapper Class for any stream that supports Position
    //and Length to make reading bytes easier
    //also has a helper function for reading all the bytes in at once
    internal class ByteStreamReader : IByteBuffer
    {
        private readonly Stream buffer;
        private int lastLenRead;

        public ByteStreamReader(Stream stream)
        {
            buffer = stream;
        }

        public long Position
        {
            get => buffer.Position;
            set
            {

                if (buffer.CanRead && value >= 0)
                    buffer.Seek(value, SeekOrigin.Begin);
            }
        }

        public long Length => buffer.CanRead ? buffer.Length : 0;

        public bool CanRead => buffer.CanRead && buffer.Position < buffer.Length;


        public ReadOnlyMemory<byte> PeekBytes(int len)
        {
            long oldPos = buffer.Position;
            Memory<byte> buf = new byte[len];

            int actualRead = buffer.Read(buf.Span);
            lastLenRead = actualRead;
            if (actualRead > 0)
            {
                buffer.Seek(oldPos, SeekOrigin.Begin);
                return buf[..actualRead];
            }

            buffer.Seek(oldPos, SeekOrigin.Begin);
            return Memory<byte>.Empty;
        }

        public byte ReadByte()
        {
            lastLenRead = buffer.ReadByte();
            if (lastLenRead > -1)
                return (byte)lastLenRead;
            return 0;
        }

        public ReadOnlyMemory<byte> ReadBytes(int len)
        {
            Memory<byte> buf = new byte[len];
            int actualRead = buffer.Read(buf.Span);
            lastLenRead = actualRead;
            return actualRead > 0 ? buf[..actualRead] : Memory<byte>.Empty;
        }

        public byte PeekByte()
        {
            byte b = ReadByte();
            buffer.Seek(-1, SeekOrigin.Current);
            return b;
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