using System;

namespace VCDiff.Shared
{
    internal interface IByteBuffer 
    {
        long Length { get; }

        long Position { get; set; }

        bool CanRead { get; }

        Memory<byte> ReadBytes(int len);

        byte ReadByte();

        Memory<byte> PeekBytes(int len);

        byte PeekByte();

        void Next();
    }
}