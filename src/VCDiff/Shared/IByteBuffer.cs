using System;

namespace VCDiff.Shared
{
    public interface IByteBuffer : IDisposable
    {
        long Length { get; }

        long Position { get; set; }

        bool CanRead { get; }

        ReadOnlyMemory<byte> ReadBytes(int len);

        byte ReadByte();

        ReadOnlyMemory<byte> PeekBytes(int len);

        byte PeekByte();

        void Skip(int len);

        void Next();

        void BufferAll();
    }
}