using System;

namespace VCDiff.Shared
{
    public interface IByteBuffer : IDisposable
    {
        long Length { get; }

        long Position { get; set; }

        bool CanRead { get; }

        byte[] ReadBytes(int len);

        byte ReadByte();

        byte[] PeekBytes(int len);

        byte PeekByte();

        void Skip(int len);

        void Next();

        void BufferAll();

        void Dispose();
    }
}