using System;

namespace VCDiff.Shared
{
    internal static class BufferReads
    {
        internal static bool TryReadExactly<T>(ref T buffer, Span<byte> destination) where T : IByteBuffer
        {
            while (!destination.IsEmpty)
            {
                int count = buffer.ReadBytesToSpan(destination).Length;
                if (count == 0) return false;
                destination = destination.Slice(count);
            }
            return true;
        }
    }
}
