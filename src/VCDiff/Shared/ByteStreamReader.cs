using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace VCDiff.Shared
{
    //Wrapper Class for any stream that supports Position
    //and Length to make reading bytes easier
    //also has a helper function for reading all the bytes in at once
    public class ByteStreamReader : IByteBuffer
    {
        private const int CACHE_SIZE = 8192;

        private readonly Stream buffer;
        private int lastLenRead;
        private byte[] cache;
        private bool _isDisposed = false;

        public ByteStreamReader(Stream stream)
        {
            cache  = ArrayPool<byte>.Shared.Rent(CACHE_SIZE);
            buffer = stream;
        }

        ~ByteStreamReader() => Dispose();

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
        public Span<byte> ReadBytesToSpan(Span<byte> data)
        {
            int bytesRead = buffer.Read(data);
            return data.Slice(0, bytesRead);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte()
        {
            lastLenRead = buffer.ReadByte();
            if (lastLenRead > -1)
                return (byte)lastLenRead;
            return 0;
        }

#if NET5_0 || NET5_0_OR_GREATER
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        public Span<byte> ReadBytesAsSpan(int len)
        {
            byte[] buf = GetCachedBuffer(len);
            int actualRead = buffer.Read(buf.AsSpanFast(len));
            lastLenRead = actualRead;
            return actualRead > 0 ? buf.AsSpanFast(actualRead) : Span<byte>.Empty;
        }

#if NET5_0 || NET5_0_OR_GREATER
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        public Memory<byte> ReadBytes(int len)
        {
            byte[] buf = GetCachedBuffer(len);
            int actualRead = buffer.Read(buf.AsSpanFast(len));
            lastLenRead = actualRead;
            return actualRead > 0 ? buf.AsMemory(0, actualRead) : Memory<byte>.Empty;
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

        public void Dispose()
        {
            if (!_isDisposed)
            {
                ArrayPool<byte>.Shared.Return(cache, false);
                _isDisposed = true;
            }

            GC.SuppressFinalize(this);
        }

#if NET5_0 || NET5_0_OR_GREATER
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private byte[] GetCachedBuffer(int len)
        {
            if (len <= CACHE_SIZE)
                return cache;

#if NET5_0 || NET5_0_OR_GREATER
            return GC.AllocateUninitializedArray<byte>(len);
#else
            return new byte[len];
#endif
        }
    }
}