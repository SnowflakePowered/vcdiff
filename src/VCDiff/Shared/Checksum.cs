using System;
using System.Runtime.CompilerServices;

namespace VCDiff.Shared
{
    internal class Checksum
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ComputeGoogleAdler32(ReadOnlySpan<byte> buffer)
        {
            return Adler32.Hash(0, buffer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ComputeXdelta3Adler32(ReadOnlySpan<byte> buffer)
        {
            return Adler32.Hash(1, buffer);
        }
    }
}