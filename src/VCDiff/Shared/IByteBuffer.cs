using System;
using System.Runtime.CompilerServices;

namespace VCDiff.Shared
{
    /// <summary>Provides a positionable reader for dictionary or patch bytes.</summary>
    public interface IByteBuffer : IDisposable
    {
        /// <summary>Gets the input length in bytes.</summary>
        long Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
        }

        /// <summary>Gets or sets the zero-based offset of the next byte to read.</summary>
        long Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set;
        }

        /// <summary>Gets whether there are more bytes to read at the current position.</summary>
        bool CanRead
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get; 
        }

        /// <summary>Reads up to the requested byte count and advances the position.</summary>
        /// <param name="len">Maximum number of bytes to read.</param>
        /// <returns>The bytes read. Copy them before another read if the implementation reuses storage.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Memory<byte> ReadBytes(int len);

        /// <summary>Reads up to the requested byte count and advances the position.</summary>
        /// <param name="len">Maximum number of bytes to read.</param>
        /// <returns>A span of the bytes read, which may reference reusable storage.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Span<byte> ReadBytesAsSpan(int len);

        /// <summary>Reads into the destination and advances by the number of bytes read.</summary>
        /// <param name="data">The destination and maximum read length.</param>
        /// <returns>A span containing the bytes read.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Span<byte> ReadBytesToSpan(Span<byte> data);

        /// <summary>Reads the byte at the current position and advances by one.</summary>
        /// <returns>The byte read. Check <see cref="CanRead"/> before calling.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        byte ReadByte();

        /// <summary>Reads the current byte while preserving the position.</summary>
        /// <returns>The current byte. Check <see cref="CanRead"/> before calling.</returns>
        byte PeekByte();

        /// <summary>Advances the position by one byte.</summary>
        void Next();
    }
}