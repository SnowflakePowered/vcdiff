using System;
using System.Runtime.CompilerServices;
#pragma warning disable 1591

namespace VCDiff.Shared
{
    public interface IByteBuffer : IDisposable
    {
        int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get; 
        }

        int Position
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
        Span<byte> ReadBytesAsSpan(int len);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        byte ReadByte();

        byte PeekByte();

        void Next();
    }
}