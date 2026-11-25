using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace VCDiff.Shared
{
    /// <summary>Reads a seekable stream through the byte-buffer interface.</summary>
    public class ByteStreamReader : IByteBuffer
    {
        private const int CACHE_SIZE = 8192;

        private readonly Stream buffer;
        private int lastLenRead;
        private byte[] cache;
        private bool _isDisposed = false;

        /// <summary>Wraps a stream and rents a buffer for small reads.</summary>
        /// <param name="stream">Caller-owned readable, seekable stream. Disposing the reader leaves it open.</param>
        public ByteStreamReader(Stream stream)
        {
            cache  = ArrayPool<byte>.Shared.Rent(CACHE_SIZE);
            buffer = stream;
        }

        /// <summary>Returns the pooled buffer when the reader is finalized.</summary>
        ~ByteStreamReader() => Dispose();

        /// <inheritdoc/>
        public long Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => buffer.Position;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => buffer.Seek(value, SeekOrigin.Begin);
        }

        /// <inheritdoc/>
        public long Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => buffer.CanRead ? buffer.Length : 0;
        }

        /// <inheritdoc/>
        public bool CanRead
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => buffer.CanRead && buffer.Position < buffer.Length;
        }

        /// <summary>Reads up to the destination length and advances by the count read.</summary>
        /// <param name="data">Destination for the bytes.</param>
        /// <returns>The written prefix of the destination.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<byte> ReadBytesToSpan(Span<byte> data)
        {
            int bytesRead = buffer.Read(data);
            return data.Slice(0, bytesRead);
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte()
        {
            lastLenRead = buffer.ReadByte();
            if (lastLenRead > -1)
                return (byte)lastLenRead;
            return 0;
        }

        /// <summary>Reads up to the requested byte count and advances the position.</summary>
        /// <param name="len">Maximum number of bytes to read.</param>
        /// <returns>The bytes read. Small reads share temporary storage that can be overwritten by the next read. Copy data that must outlive the reader or another read.</returns>
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

        /// <summary>Reads up to the requested byte count and advances the position.</summary>
        /// <param name="len">Maximum number of bytes to read.</param>
        /// <returns>The bytes read. Small reads share temporary storage that can be overwritten by the next read. Copy data that must outlive the reader or another read.</returns>
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

        /// <summary>Reads from the stream into the supplied span.</summary>
        /// <param name="buf">Destination for bytes read from the current stream position.</param>
        /// <returns>The actual byte count, or zero at end of stream.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadBytesIntoBuf(Span<byte> buf)
        {
            int actualRead = buffer.Read(buf);
            lastLenRead = actualRead;
            return actualRead;
        }

        /// <summary>Asynchronously reads from the stream into the supplied memory.</summary>
        /// <param name="buf">Destination for bytes read from the current stream position.</param>
        /// <returns>A task returning the actual byte count, or zero at end of stream.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<int> ReadBytesIntoBufAsync(Memory<byte> buf)
        {
            int actualRead = await buffer.ReadAsync(buf);
            lastLenRead = actualRead;
            return actualRead;
        }

        /// <inheritdoc/>
        public byte PeekByte()
        {
            byte b = ReadByte();
            buffer.Seek(-1, SeekOrigin.Current);
            return b;
        }

        //increases the offset by 1
        /// <inheritdoc/>
        public void Next()
        {
            buffer.Seek(1, SeekOrigin.Current);
        }

        /// <summary>Returns the rented read buffer to the pool and leaves the stream open.</summary>
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