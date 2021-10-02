using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace VCDiff.Shared
{
    internal static class Extensions
    {
        public static Span<byte> AsSpanFast(this byte[] data)
        {
#if NET5_0
            return MemoryMarshal.CreateSpan(ref MemoryMarshal.GetArrayDataReference(data), data.Length);
#else
            return data.AsSpan();
#endif
        }

        public static Span<byte> AsSpanFast(this byte[] data, int length)
        {
#if NET5_0
            return MemoryMarshal.CreateSpan(ref MemoryMarshal.GetArrayDataReference(data), length);
#else
            return data.AsSpan(0, length);
#endif
        }

    }
}
