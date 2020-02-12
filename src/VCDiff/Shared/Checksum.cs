using System;

namespace VCDiff.Shared
{
    internal class Checksum
    {
        public static uint ComputeGoogleAdler32(ReadOnlyMemory<byte> buffer)
        {
            return Adler32.Hash(0, buffer.Span);
        }

        public static uint ComputeXdelta3Adler32(ReadOnlyMemory<byte> buffer)
        {
            return Adler32.Hash(1, buffer.Span);
        }

        public static long UpdateAdler32(uint partial, ReadOnlyMemory<byte> buffer)
        {
            return Adler32.Hash(partial, buffer.Span);
        }
    }
}