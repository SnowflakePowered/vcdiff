using System;
using System.Buffers;
using System.Linq;

namespace VCDiff.Shared
{
    public class ByteBuffer : IByteBuffer, IDisposable
    internal class ByteBuffer : IByteBuffer, IDisposable
    {
        private ReadOnlySequence<byte> Sequence { get; }

        /// <summary>
        /// Basically a simple wrapper for byte[] arrays
        /// for easier reading and parsing
        /// </summary>
        /// <param name="bytes"></param>
        public ByteBuffer(byte[] bytes)
        {
            this.Sequence = new ReadOnlySequence<byte>(bytes ?? new byte[] { });
            this.Position = 0;
        }

        /// <summary>
        /// Basically a simple wrapper for byte[] arrays
        /// for easier reading and parsing
        /// </summary>
        /// <param name="bytes"></param>
        public ByteBuffer(ReadOnlyMemory<byte> bytes)
        {
            this.Sequence = new ReadOnlySequence<byte>(bytes);
            this.Position = 0;
        }

        public bool CanRead => this.Position < this.Length;

        public long Position
        {
            get => this.offset;
            set
            {
                if (value > this.Sequence.Length || value < 0) return;
                this.offset = value;
            }
        }

        public void BufferAll()
        {
            //not implemented in this one
            //since it already contains the full buffered data
        }

        public long Length => this.Sequence.Length;

        private long offset;

        public byte PeekByte()
        {
            if (this.Position >= this.Length) throw new ArgumentOutOfRangeException("Attempted to read past end of buffer.");
            return this.Sequence.Slice(this.Position, this.Position + 1).FirstSpan[0];
        }

        public ReadOnlyMemory<byte> PeekBytes(int len)
        {
            long realLen = (int)this.Position + len > this.Sequence.Length ? this.Sequence.Length - (int)this.Position : len;
            return this.Sequence.Slice(this.Position, realLen).First;
        }

        public byte ReadByte()
        {
            if (this.Position >= this.Length) throw new Exception("Trying to read past End of Buffer");
            byte value = this.PeekByte();
            this.Position++;
            return value;
        }

        public ReadOnlyMemory<byte> ReadBytes(int len)
        {
            var values = this.PeekBytes(len);
            this.Position += len;
            return values;
        }

        public void Next()
        {
            this.Position++;
        }

        public void Skip(int len)
        {
            this.Position += len;
        }

        public void Dispose()
        {
        }
    }
}