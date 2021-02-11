using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace VCDiff.Shared
{
    internal class ByteBuffer : IByteBuffer, IDisposable
    {
        private Memory<byte> bytes;
        private MemoryHandle byteHandle;
        private unsafe byte* bytePtr;
        private int length;
        private int offset;
        private byte[]? buf;
        private MemoryStream? copyStream;

        private ByteBuffer()
        {

        }

        public ByteBuffer(Stream copyStream)
        {
            this.copyStream = new MemoryStream();
            copyStream.CopyTo(this.copyStream);
            this.copyStream.Seek(0, SeekOrigin.Begin);
            offset = 0;
            this.buf = this.copyStream.GetBuffer();
            this.bytes = new Memory<byte>(buf, 0, (int)copyStream.Length);
            this.byteHandle = this.bytes.Pin();

            unsafe
            {
                this.bytePtr = (byte*)this.byteHandle.Pointer;
            }

            length = this.bytes.Length;
        }

        public static async Task<ByteBuffer> CreateBufferAsync(Stream copyStream)
        {
            var buffer = new ByteBuffer { copyStream = new MemoryStream() };
            await copyStream.CopyToAsync(buffer.copyStream);
            buffer.copyStream.Seek(0, SeekOrigin.Begin);
            buffer.offset = 0;
            buffer.buf = buffer.copyStream.GetBuffer();
            buffer.bytes = new Memory<byte>(buffer.buf, 0, (int)copyStream.Length);
            buffer.byteHandle = buffer.bytes.Pin();
            unsafe
            {
                buffer.bytePtr = (byte*)buffer.byteHandle.Pointer;
            }

            buffer.length = buffer.bytes.Length;

            return buffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte[]? DangerousGetMemoryStreamBuffer() => buf;

        /// <summary>
        /// Dangerously gets the byte pointer.
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe byte* DangerousGetBytePointer()
        {
            return bytePtr;
        }

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
        /// <summary>
        /// Basically a simple wrapper for byte[] arrays
        /// for easier reading and parsing
        /// </summary>
        /// <param name="bytes"></param>
        public ByteBuffer(byte[] bytes)
        {
            offset = 0;
            this.buf = bytes;
            this.bytes = bytes != null ? new Memory<byte>(bytes) : Memory<byte>.Empty;
            this.byteHandle = this.bytes.Pin();
            unsafe
            {
                this.bytePtr = (byte*)this.byteHandle.Pointer;
            }

            length = this.bytes.Length;
        }

        public ByteBuffer(Memory<byte> bytes)
        {
            offset = 0;
            this.bytes = bytes;
            this.byteHandle = bytes.Pin();
            unsafe
            {
                this.bytePtr = (byte*)this.byteHandle.Pointer;
            }
            length = this.bytes.Length;
        }

        public bool CanRead
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => offset < length;
        }

        public long Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => offset;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            // We used to check, but this is never true in calls. if (value > length || value < 0) return;
            set => offset = (int)value;
        }

        public long Length {         
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte PeekByte()
        {
            unsafe
            {
                return *((byte*)this.bytePtr + offset);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Memory<byte> PeekBytes(int len)
        {
            int sliceLen = offset + len > this.length ? this.length - offset : len;
            return bytes.Slice(offset, sliceLen);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte()
        {
            unsafe
            {
                return this.bytePtr[offset++];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Memory<byte> ReadBytes(int len)
        {
            var slice = PeekBytes(len);
            offset += len;
            return slice;
        }

        public void Next()
        {
            offset++;
        }

        public void Dispose()
        {
            this.byteHandle.Dispose();
            this.copyStream?.Dispose();
        }
    }
}