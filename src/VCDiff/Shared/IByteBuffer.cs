using System;
using System.Runtime.CompilerServices;

namespace VCDiff.Shared
{
    internal interface IByteBuffer 
    {
        long Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get; 
        }

        long Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set; 
        }

        bool CanRead
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get; 
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Memory<byte> ReadBytes(int len);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        byte ReadByte();

        Memory<byte> PeekBytes(int len);

        byte PeekByte();

        void Next();
    }
}