﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

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
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => buffer.Position;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {

                if (buffer.CanRead && value >= 0)
                    buffer.Seek(value, SeekOrigin.Begin);
            }
        }

        public long Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => buffer.CanRead? buffer.Length: 0;
        }

        public bool CanRead
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => buffer.CanRead && buffer.Position < buffer.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Memory<byte> PeekBytes(int len)
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte()
        {
            lastLenRead = buffer.ReadByte();
            if (lastLenRead > -1)
                return (byte)lastLenRead;
            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Memory<byte> ReadBytes(int len)
        {
            Memory<byte> buf = new byte[len];
            int actualRead = buffer.Read(buf.Span);
            lastLenRead = actualRead;
            return actualRead > 0 ? buf[..actualRead] : Memory<byte>.Empty;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadBytesIntoBuf(Span<byte> buf)
        {
            int actualRead = buffer.Read(buf);
            lastLenRead = actualRead;
            return actualRead;
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
    }
}