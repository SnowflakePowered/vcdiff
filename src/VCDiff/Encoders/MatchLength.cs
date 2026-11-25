// SPDX-License-Identifier: Apache-2.0
// Adapted from DeltaSharp. Copyright (c) 2024 Israel Lot.
using System;
using System.Runtime.CompilerServices;
#if NET8_0_OR_GREATER
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
#endif

namespace VCDiff.Encoders
{
    internal static class MatchLength
    {
#if !NET8_0_OR_GREATER
        public static int MatchForward(ReadOnlySpan<byte> x, ReadOnlySpan<byte> y)
        {
            int length = Math.Min(x.Length, y.Length);
            int matched = 0;
            while (matched < length && x[matched] == y[matched])
                matched++;
            return matched;
        }

        public static int MatchBackward(ReadOnlySpan<byte> x, ReadOnlySpan<byte> y)
        {
            int length = Math.Min(x.Length, y.Length);
            int matched = 0;
            while (matched < length && x[x.Length - 1 - matched] == y[y.Length - 1 - matched])
                matched++;
            return matched;
        }
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MatchForward(ReadOnlySpan<byte> x, ReadOnlySpan<byte> y)
        {
            int length = Math.Min(x.Length, y.Length);
            ref byte first = ref MemoryMarshal.GetReference(x);
            ref byte second = ref MemoryMarshal.GetReference(y);
            if (!Vector128.IsHardwareAccelerated || length < Vector128<byte>.Count)
                return MatchForwardScalar(ref first, ref second, length);

            // Most candidates differ in the first vector. Keep that comparison in the caller.
            ulong mask = EqualByteMask(ref first, ref second, 0);
            if (mask != AllEqualMask)
                return MatchingPrefixBytes(mask);

            return length == Vector128<byte>.Count ? length : MatchForwardTail(ref first, ref second, length);
        }

        private static int MatchForwardTail(ref byte first, ref byte second, int length)
        {
            nuint last = (nuint)length - (nuint)Vector128<byte>.Count;
            for (nuint offset = (nuint)Vector128<byte>.Count; offset < last; offset += (nuint)Vector128<byte>.Count)
            {
                ulong mask = EqualByteMask(ref first, ref second, offset);
                if (mask != AllEqualMask)
                    return (int)offset + MatchingPrefixBytes(mask);
            }

            // The final vector ends at the shorter span's boundary and can overlap the previous one.
            ulong finalMask = EqualByteMask(ref first, ref second, last);
            return finalMask == AllEqualMask ? length : (int)last + MatchingPrefixBytes(finalMask);
        }

        private static int MatchForwardScalar(ref byte first, ref byte second, int length)
        {
            int i = 0;
            for (; i < length - 8; i += 8)
            {
                ulong difference = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref first, i))
                                 ^ Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref second, i));
                if (difference != 0)
                {
                    int bits = BitConverter.IsLittleEndian ? BitOperations.TrailingZeroCount(difference) : BitOperations.LeadingZeroCount(difference);
                    return i + bits / 8;
                }
            }

            while (i < length && Unsafe.Add(ref first, i) == Unsafe.Add(ref second, i))
                i++;
            return i;
        }

        public static int MatchBackward(ReadOnlySpan<byte> x, ReadOnlySpan<byte> y)
        {
            var length = Math.Min(x.Length, y.Length);

            if (!Vector128.IsHardwareAccelerated || length < 16)
            {
                ref byte xPtr = ref Unsafe.Add(ref MemoryMarshal.GetReference(x), x.Length - 1);
                ref byte yPtr = ref Unsafe.Add(ref MemoryMarshal.GetReference(y), y.Length - 1);

                int i = 0;
                ulong difference = 0;

                for (; i < length - 8; i = i + 8)
                {
                    var xlong = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Subtract(ref xPtr, (i + 7)));
                    var ylong = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Subtract(ref yPtr, (i + 7)));
                    difference = (xlong ^ ylong);

                    if (difference != 0)
                    {
                        int bits = BitConverter.IsLittleEndian ? BitOperations.LeadingZeroCount(difference) : BitOperations.TrailingZeroCount(difference);
                        return i + bits / 8;
                    }
                }

                for (; i < length; i++)
                {
                    if (Unsafe.Add(ref xPtr, -i) != Unsafe.Add(ref yPtr, -i))
                        break;
                }

                return i;
            }

            int offset = length - Vector128<byte>.Count;
            ref byte first = ref Unsafe.Add(ref MemoryMarshal.GetReference(x), x.Length - length);
            ref byte second = ref Unsafe.Add(ref MemoryMarshal.GetReference(y), y.Length - length);

            ulong mask;
            for (; offset > 0; offset -= Vector128<byte>.Count)
            {
                mask = EqualByteMask(ref first, ref second, (nuint)offset);
                if (mask != AllEqualMask)
                    goto Found;
            }

            offset = 0;
            mask = EqualByteMask(ref first, ref second, 0);
            if (mask == AllEqualMask)
                return length;

        Found:
            int matchingSuffix = AdvSimd.Arm64.IsSupported
                ? BitOperations.LeadingZeroCount(~mask) / 4
                : ushort.LeadingZeroCount((ushort)~mask);
            return length - offset - Vector128<byte>.Count + matchingSuffix;
        }

        private static ulong AllEqualMask
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => AdvSimd.Arm64.IsSupported ? ulong.MaxValue : ushort.MaxValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong EqualByteMask(ref byte first, ref byte second, nuint offset)
        {
            var equal = Vector128.Equals(Vector128.LoadUnsafe(ref first, offset), Vector128.LoadUnsafe(ref second, offset));
            if (AdvSimd.Arm64.IsSupported)
            {
                // Each equality byte is 00 or FF. Narrow each pair to two four-bit results.
                return AdvSimd.ShiftRightLogicalNarrowingLower(equal.AsUInt16(), 4).AsUInt64().ToScalar();
            }
            return equal.ExtractMostSignificantBits();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int MatchingPrefixBytes(ulong mask) => AdvSimd.Arm64.IsSupported
            ? BitOperations.TrailingZeroCount(~mask) / 4
            : BitOperations.TrailingZeroCount((uint)~mask);
#endif
    }
}
