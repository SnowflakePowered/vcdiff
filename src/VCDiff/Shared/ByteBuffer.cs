using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace VCDiff.Shared
{
    /// <summary>
    /// Encapsulates a buffer that reads bytes from managed or unmanaged memory.
    /// </summary>
    public class ByteBuffer : IByteBuffer, IDisposable
    {
        private MemoryHandle? byteHandle;
        private unsafe byte* bytePtr;
        private long length;
        private long offset;

        private ByteBuffer()
        {

        }

        /// <summary>Releases an outstanding pin when the buffer is finalized.</summary>
        ~ByteBuffer()
        {
            Dispose();
        }

        /// <summary>Pins an array and reads its contents in place.</summary>
        /// <param name="bytes">The array to pin. A null array produces an empty buffer.</param>
        /// <remarks>Dispose the buffer to release the pin.</remarks>
        public unsafe ByteBuffer(byte[] bytes)
        {
            offset = 0;
            var memory = bytes != null ? new Memory<byte>(bytes) : Memory<byte>.Empty;
            this.byteHandle = memory.Pin();
            CreateFromPointer((byte*)this.byteHandle.Value.Pointer, memory.Length);
        }

        internal unsafe ByteBuffer(NativeAllocation<byte> bytes)
        {
            offset = 0;
            CreateFromPointer(bytes.Pointer, bytes.NumItems);
        }

        /// <summary>Pins a memory region and reads its contents in place.</summary>
        /// <param name="bytes">The memory to pin.</param>
        /// <remarks>Dispose the buffer to release the pin.</remarks>
        public unsafe ByteBuffer(Memory<byte> bytes)
        {
            offset = 0;
            this.byteHandle = bytes.Pin();
            CreateFromPointer((byte*)this.byteHandle.Value.Pointer, bytes.Length);
        }

        /// <summary>Borrows the storage referenced by a span.</summary>
        /// <param name="bytes">The bytes to expose.</param>
        /// <remarks>The caller must keep the storage alive and at a stable address for the entire buffer lifetime. For movable managed memory, use the array or Memory overload to pin it.</remarks>
        public unsafe ByteBuffer(Span<byte> bytes)
        {
            offset = 0;

            // Using GetPinnableReference because length of 0 means out of bound exception.
            CreateFromPointer((byte*)Unsafe.AsPointer(ref bytes.GetPinnableReference()), bytes.Length);
        }

        /// <summary>Borrows an unmanaged memory region.</summary>
        /// <param name="bytes">Pointer to the beginning of the region.</param>
        /// <param name="length">Number of accessible bytes from the pointer.</param>
        /// <remarks>The caller owns the memory and must keep it valid for the entire buffer lifetime.</remarks>
        public unsafe ByteBuffer(byte* bytes, int length)
        {
            offset = 0;
            CreateFromPointer(bytes, length);
        }

        private unsafe void CreateFromPointer(byte* pointer, long length)
        {
            this.bytePtr = pointer;
            this.length = length;
        }

#if NET5_0 || NET5_0_OR_GREATER || NETCOREAPP3_1 || NETSTANDARD2_1_OR_GREATER
        /// <summary>Exposes the entire buffer, independent of the current position.</summary>
        /// <returns>A writable view valid while the underlying storage is valid.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe Span<byte> AsSpan() => MemoryMarshal.CreateSpan(ref Unsafe.AsRef<byte>(bytePtr), (int)length);
#else
        /// <summary>Exposes the entire buffer, independent of the current position.</summary>
        /// <returns>A writable view valid while the underlying storage is valid.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe Span<byte> AsSpan() => new Span<byte>(bytePtr, (int)length);
#endif

        /// <summary>Gets the address of the first byte.</summary>
        /// <returns>A borrowed pointer valid while the underlying storage is valid.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe byte* DangerousGetBytePointer() => bytePtr;

        /// <summary>Gets a pointer at the current position and advances the cursor.</summary>
        /// <param name="read">Number of bytes to advance. The caller must check the range.</param>
        /// <returns>A borrowed pointer to the position before the advance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe byte* DangerousGetBytePointerAtCurrentPositionAndIncreaseOffsetAfter(int read)
        {
            byte* ptr = bytePtr + offset;
            offset += read;
            return ptr;
        }

        /// <inheritdoc/>
        public bool CanRead
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => offset < length;
        }

        /// <inheritdoc/>
        public long Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => offset;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            // We used to check, but this is never true in calls. if (value > length || value < 0) return;
            set => offset = value;
        }

        /// <inheritdoc/>
        public long Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => length;
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe byte PeekByte() => *((byte*)this.bytePtr + offset);

        /// <summary>Exposes the available bytes starting at the current position without advancing.</summary>
        /// <param name="len">Maximum number of bytes to expose.</param>
        /// <returns>A view of up to the requested number of bytes.</returns>
#if NET5_0 || NET5_0_OR_GREATER
        [SkipLocalsInit]
#endif
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe Span<byte> PeekBytes(int len)
        {
            int sliceLen = (int)(offset + len > this.length ? this.length - offset : len);
#if NET5_0 || NET5_0_OR_GREATER || NETCOREAPP3_1 || NETSTANDARD2_1_OR_GREATER
            return MemoryMarshal.CreateSpan(ref Unsafe.AsRef<byte>(bytePtr + offset), sliceLen);
#else
            return new Span<byte>(bytePtr + offset, sliceLen);
#endif
        }

        /// <summary>Reads up to the destination length and advances by the count read.</summary>
        /// <param name="data">Destination for the bytes.</param>
        /// <returns>A view of the source bytes copied into the destination.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<byte> ReadBytesToSpan(Span<byte> data)
        {
            var result = PeekBytes(data.Length);
            result.CopyTo(data);
            offset += result.Length;
            return result.Slice(0, result.Length);
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe byte ReadByte() => this.bytePtr[offset++];

        /// <summary>Reads up to the requested byte count and advances the position.</summary>
        /// <param name="len">Maximum number of bytes to read.</param>
        /// <returns>A view into the underlying storage.</returns>
#if NET5_0 || NET5_0_OR_GREATER
        [SkipLocalsInit]
#endif
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<byte> ReadBytesAsSpan(int len)
        {
            var slice = PeekBytes(len);
            offset += len;
            return slice;
        }

        /// <summary>Reads up to the requested byte count and advances the position.</summary>
        /// <param name="len">Maximum number of bytes to read.</param>
        /// <returns>An independently allocated copy of the bytes read.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Memory<byte> ReadBytes(int len)
        {
            var slice = PeekBytes(len);
            offset += len;
            return slice.ToArray();
        }

        /// <inheritdoc/>
        public void Next() => offset++;

        /// <summary>Releases any managed-memory pin held by the buffer.</summary>
        public void Dispose()
        {
            this.byteHandle?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}