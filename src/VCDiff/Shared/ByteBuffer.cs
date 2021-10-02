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
        private unsafe byte*  bytePtr;
        private int length;
        private int offset;

        private ByteBuffer()
        {

        }

        /// <summary/>
        public unsafe ByteBuffer(byte[] bytes)
        {
            offset = 0;
            var memory      = bytes != null ? new Memory<byte>(bytes) : Memory<byte>.Empty;
            this.byteHandle = memory.Pin();
            CreateFromPointer((byte*)this.byteHandle.Value.Pointer, memory.Length);
        }

        /// <summary/>
        public unsafe ByteBuffer(Memory<byte> bytes)
        {
            offset = 0;
            this.byteHandle = bytes.Pin();
            CreateFromPointer((byte*)this.byteHandle.Value.Pointer, bytes.Length);
        }

        /// <summary/>
        public unsafe ByteBuffer(Span<byte> bytes)
        {
            offset = 0;

            // Using GetPinnableReference because length of 0 means out of bound exception.
            CreateFromPointer((byte*)Unsafe.AsPointer(ref bytes.GetPinnableReference()), bytes.Length);
        }

        /// <summary/>
        public unsafe ByteBuffer(byte* bytes, int length)
        {
            offset = 0;
            CreateFromPointer(bytes, length);
        }

        private unsafe void CreateFromPointer(byte* pointer, int length)
        {
            this.bytePtr = pointer;
            this.length = length;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe Span<byte> AsSpan() => MemoryMarshal.CreateSpan(ref Unsafe.AsRef<byte>(bytePtr), length);

        /// <summary>
        /// Dangerously gets the byte pointer.
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe byte* DangerousGetBytePointer() => bytePtr;

        /// <summary>
        /// Dangerously retrieves the byte pointer at the current position and then increases the offset after.
        /// </summary>
        /// <param name="read"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe byte* DangerousGetBytePointerAtCurrentPositionAndIncreaseOffsetAfter(int read)
        {
            byte* ptr = bytePtr + offset;
            offset += read;
            return ptr;
        }

        public bool CanRead
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => offset < length;
        }

        public int Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => offset;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            // We used to check, but this is never true in calls. if (value > length || value < 0) return;
            set => offset = value;
        }

        public int Length {         
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe byte PeekByte() => *((byte*)this.bytePtr + offset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe Span<byte> PeekBytes(int len)
        {
            int sliceLen = offset + len > this.length ? this.length - offset : len;
            return MemoryMarshal.CreateSpan(ref Unsafe.AsRef<byte>(bytePtr + offset), sliceLen);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe byte ReadByte() => this.bytePtr[offset++];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<byte> ReadBytesAsSpan(int len)
        {
            var slice = PeekBytes(len);
            offset += len;
            return slice;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Memory<byte> ReadBytes(int len)
        {
            var slice = PeekBytes(len);
            offset += len;
            return slice.ToArray();
        }

        public void Next() => offset++;

        public void Dispose() => this.byteHandle?.Dispose();
    }
}