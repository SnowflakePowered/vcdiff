using System;
using System.Collections.Generic;
using System.IO;

namespace VCDiff.Shared
{
    //Wrapper Class for any stream that supports Position
    //and Length to make reading bytes easier
    //also has a helper function for reading all the bytes in at once
    internal class ByteStreamReader : IByteBuffer, IDisposable
    {
        private Stream buffer;
        private int lastLenRead;
        private bool readAll;
        private byte[] internalBuffer;
        private long offset;

        public ByteStreamReader(Stream stream)
        {
            buffer = stream;
            internalBuffer = new byte[stream.Length];
        }

        public long Position
        {
            get
            {
                if (readAll)
                {
                    return offset;
                }
                return buffer.Position;
            }
            set
            {
                if (readAll)
                {
                    if (value >= 0)
                        offset = value;
                }
                if (buffer.CanRead && value >= 0)
                    buffer.Position = value;
            }
        }

        public long Length
        {
            get
            {
                if (readAll)
                {
                    return internalBuffer.Length;
                }

                if (buffer.CanRead)
                    return buffer.Length;

                return 0;
            }
        }

        public bool CanRead
        {
            get
            {
                if (readAll)
                {
                    return offset < internalBuffer.Length;
                }

                return buffer.CanRead && buffer.Position < buffer.Length;
            }
        }

        public void BufferAll()
        {
            if (!readAll)
            {
                offset = 0;
                Span<byte> buff = new byte[1024 * 8];

                int bytesCopied = 0;

                do
                {
                    lastLenRead = buffer.Read(buff);
                    buff[0..lastLenRead].CopyTo(internalBuffer.AsSpan(bytesCopied));
                    bytesCopied += lastLenRead;
                }
                while (lastLenRead > 0 && buffer.CanRead);
                readAll = true;
            }
        }

        public ReadOnlyMemory<byte> PeekBytes(int len)
        {
            if (readAll)
            {
                int sliceLen = offset + len > internalBuffer.Length ? internalBuffer.Length - (int)offset : len;
                return this.internalBuffer.AsMemory().Slice((int)offset, sliceLen);
            }

            long oldPos = buffer.Position;
            Memory<byte> buf = new byte[len];

            int actualRead = buffer.Read(buf.Span);
            lastLenRead = actualRead;
            if (actualRead > 0)
            {
                buffer.Position = oldPos;
                return buf[0..actualRead];
            }

            buffer.Position = oldPos;
            return Memory<byte>.Empty;
        }

        public byte ReadByte()
        {
            if (!CanRead) throw new Exception("Trying to read past end of buffer");
            if (readAll)
            {
                return internalBuffer[(int)offset++];
            }
            lastLenRead = buffer.ReadByte();
            if (lastLenRead > -1)
                return (byte)lastLenRead;
            return 0;
        }

        public ReadOnlyMemory<byte> ReadBytes(int len)
        {
            if (readAll)
            {
                int sliceLen = offset + len > internalBuffer.Length ? internalBuffer.Length - (int)offset : len;
                var slice = this.internalBuffer.AsMemory().Slice((int)offset, sliceLen);
                offset += len;
                return slice;
            }

            Memory<byte> buf = new byte[len];
            int actualRead = buffer.Read(buf.Span);
            lastLenRead = actualRead;
            if (actualRead > 0)
            {
                return buf[0..actualRead];
            }

            return Memory<byte>.Empty;
        }

        public byte PeekByte()
        {
            if (!CanRead) throw new Exception("Trying to read past end of buffer");
            if (readAll)
            {
                return internalBuffer[(int)offset];
            }
            long lastPos = buffer.Position;
            byte b = ReadByte();
            buffer.Position = lastPos;
            return b;
        }

        //increases the offset by 1
        public void Next()
        {
            buffer.Position++;
        }

        public void Skip(int len)
        {
            buffer.Position += len;
        }

        public void Dispose()
        {
            buffer.Dispose();
        }
    }
}