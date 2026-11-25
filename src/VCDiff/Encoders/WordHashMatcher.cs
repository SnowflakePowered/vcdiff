// SPDX-License-Identifier: Apache-2.0
// Adapted from DeltaSharp. Copyright (c) 2024 Israel Lot.
using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using VCDiff.Shared;

namespace VCDiff.Encoders
{
    internal static class WordHashMatcher
    {
        internal static IWindowMatcher Create(ByteBuffer source, int blockSize, int candidateLimit, IndexPreference preference)
            => blockSize switch
            {
                8 => Create<WordBlock8>(source, candidateLimit, preference),
                16 => Create<WordBlock16>(source, candidateLimit, preference),
                32 => Create<WordBlock32>(source, candidateLimit, preference),
                _ => throw new ArgumentOutOfRangeException(nameof(blockSize))
            };

        private static IWindowMatcher Create<TBlock>(ByteBuffer source, int candidateLimit, IndexPreference preference)
            where TBlock : struct, IWordBlock
        {
            int blocks = (int)source.Length / default(TBlock).Size;
            if (blocks <= byte.MaxValue)
                return new WordHashMatcher<byte, TBlock>(source, candidateLimit, preference);
            if (blocks <= ushort.MaxValue)
                return new WordHashMatcher<ushort, TBlock>(source, candidateLimit, preference);
            return new WordHashMatcher<int, TBlock>(source, candidateLimit, preference);
        }
    }

    internal sealed class WordHashMatcher<T, TBlock> : IWindowMatcher
        where T : unmanaged
        where TBlock : struct, IWordBlock
    {
        private readonly ByteBuffer dictionary;
        private readonly int bucketCount;
        private readonly int candidateLimit;
        // Bucket heads followed by collision links. Zero means empty, indices are one-based.
        private readonly T[] index;

        internal WordHashMatcher(ByteBuffer dictionary, int candidateLimit, IndexPreference preference)
        {
            this.dictionary = dictionary;
            this.candidateLimit = candidateLimit;
            int blockSize = default(TBlock).Size;
            ReadOnlySpan<byte> source = dictionary.AsSpan();
            int blocks = source.Length / blockSize;
            int buckets = 256;
            while (buckets < blocks)
                buckets <<= 1;
            // At most 255 blocks fit in a byte index; its 256 buckets suffice in either mode.
            if (typeof(T) != typeof(byte) && preference == IndexPreference.Speed)
                buckets *= 2;
            bucketCount = buckets;
            index = ArrayPool<T>.Shared.Rent(checked(buckets + blocks));
            // Each live collision link is overwritten below; only the bucket heads need clearing.
            Array.Clear(index, 0, buckets);
            int bucketMask = buckets - 1;
            for (int block = blocks - 1; block >= 0; block--)
            {
                int bucket = Bucket(source.Slice(block * blockSize, blockSize), bucketMask);
                index[buckets + block] = index[bucket];
                index[bucket] = Pack(block + 1);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Bucket(ReadOnlySpan<byte> block, int bucketMask)
        {
            unchecked
            {
                ulong hash = default(TBlock).Seed(block);
                // Stafford Mix13, as described at https://nullprogram.com/blog/2018/07/31/
                hash ^= hash >> 30;
                hash *= 0xBF58476D1CE4E5B9UL;
                hash ^= hash >> 27;
                hash *= 0x94D049BB133111EBUL;
                hash ^= hash >> 31;
                return (int)hash & bucketMask;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T Pack(int value)
        {
            if (typeof(T) == typeof(byte))
            {
                byte small = (byte)value;
                return Unsafe.As<byte, T>(ref small);
            }
            if (typeof(T) == typeof(ushort))
            {
                ushort small = (ushort)value;
                return Unsafe.As<ushort, T>(ref small);
            }
            return Unsafe.As<int, T>(ref value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Unpack(T value)
        {
            if (typeof(T) == typeof(byte)) return Unsafe.As<T, byte>(ref value);
            if (typeof(T) == typeof(ushort)) return Unsafe.As<T, ushort>(ref value);
            return Unsafe.As<T, int>(ref value);
        }

        public void Encode(ReadOnlySpan<byte> target, WindowEncoder window, int minimumCopyLength)
        {
            ReadOnlySpan<byte> source = dictionary.AsSpan();
            int bucketMask = bucketCount - 1;
            int blockSize = default(TBlock).Size;
            int unwritten = 0;
            int probe = 0;
            while (probe <= target.Length - blockSize)
            {
                byte firstByte = target[probe];
                int bucket = Bucket(target.Slice(probe, blockSize), bucketMask);
                int bestLength = 0, bestSource = 0, bestLeft = 0;
                int remaining = candidateLimit;
                for (int block = Unpack(index[bucket]) - 1; block >= 0 && remaining-- > 0;
                    block = Unpack(index[bucketCount + block]) - 1)
                {
                    int offset = block * blockSize;
                    if (source[offset] != firstByte)
                        continue;

                    int right = MatchLength.MatchForward(target.Slice(probe), source.Slice(offset));
                    int left = 0;
                    if (probe > unwritten && offset > 0 && target[probe - 1] == source[offset - 1])
                        left = MatchLength.MatchBackward(target.Slice(unwritten, probe - unwritten), source.Slice(0, offset));
                    if (left + right > bestLength)
                    {
                        bestLength = left + right;
                        bestSource = offset - left;
                        bestLeft = left;
                        if (bestLength == target.Length - unwritten)
                            break;
                    }
                }
                if (bestLength < minimumCopyLength)
                {
                    probe++;
                    continue;
                }

                int start = probe - bestLeft;
                if (start > unwritten)
                    window.Add(target.Slice(unwritten, start - unwritten));
                window.Copy(bestSource, bestLength);
                unwritten = start + bestLength;
                probe = unwritten;
            }
            if (unwritten < target.Length)
                window.Add(target.Slice(unwritten));
        }

        public void Dispose() => ArrayPool<T>.Shared.Return(index);
    }
}
