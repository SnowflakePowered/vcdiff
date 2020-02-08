using System;

namespace VCDiff.Shared
{
    public class Checksum
    {
        public static uint ComputeAdler32(ReadOnlyMemory<byte> buffer)
        {
            return Adler32.Hash(0, buffer.Span);
        }

        public static long UpdateAdler32(uint partial, ReadOnlyMemory<byte> buffer)
        {
            return Adler32.Hash(partial, buffer.Span);
        }
    }
}