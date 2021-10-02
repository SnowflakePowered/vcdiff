using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace VCDiff.Shared
{
    //Wrapper Class for any stream that supports Position
    //and Length to make reading bytes easier
    //also has a helper function for reading all the bytes in at once
    public class ByteStreamReader : IByteBuffer
    {
        private readonly Stream buffer;
        private int lastLenRead;

        public ByteStreamReader(Stream stream)
        {
            buffer = stream;
        }

        public int Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int) buffer.Position;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => buffer.Seek(value, SeekOrigin.Begin);
        }

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)(buffer.CanRead ? buffer.Length : 0);
        }

        public bool CanRead
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => buffer.CanRead && buffer.Position < buffer.Length;
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
        public Span<byte> ReadBytesAsSpan(int len)
        {
            var buf = new byte[len];
            int actualRead = buffer.Read(buf, 0, buf.Length);
            lastLenRead = actualRead;
            return actualRead > 0 ? buf.AsSpan()[..actualRead] : Span<byte>.Empty;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Memory<byte> ReadBytes(int len)
        {
            var buf = new byte[len];
            int actualRead = buffer.Read(buf, 0, buf.Length);
            lastLenRead = actualRead;
            return actualRead > 0 ? buf.AsMemory()[..actualRead] : Memory<byte>.Empty;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadBytesIntoBuf(Span<byte> buf)
        {
            int actualRead = buffer.Read(buf);
            lastLenRead = actualRead;
            return actualRead;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<int> ReadBytesIntoBufAsync(Memory<byte> buf)
        {
            int actualRead = await buffer.ReadAsync(buf);
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

        public void Dispose() { }
    }
}