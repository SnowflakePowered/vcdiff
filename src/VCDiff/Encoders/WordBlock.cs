// SPDX-License-Identifier: Apache-2.0
// Adapted from DeltaSharp. Copyright (c) 2024 Israel Lot.
using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace VCDiff.Encoders
{
    internal interface IWordBlock
    {
        int Size { get; }
        ulong Seed(ReadOnlySpan<byte> block);
    }

    internal readonly struct WordBlock8 : IWordBlock
    {
        public int Size => 8;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong Seed(ReadOnlySpan<byte> block) => BinaryPrimitives.ReadUInt64LittleEndian(block);
    }

    internal readonly struct WordBlock16 : IWordBlock
    {
        public int Size => 16;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong Seed(ReadOnlySpan<byte> block)
            => Fold(BinaryPrimitives.ReadUInt64LittleEndian(block), block.Slice(8));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong Fold(ulong seed, ReadOnlySpan<byte> nextWord)
            => unchecked(seed * 0x9E3779B97F4A7C15UL + BinaryPrimitives.ReadUInt64LittleEndian(nextWord));
    }

    internal readonly struct WordBlock32 : IWordBlock
    {
        public int Size => 32;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong Seed(ReadOnlySpan<byte> block)
        {
            ulong seed = default(WordBlock16).Seed(block);
            seed = WordBlock16.Fold(seed, block.Slice(16));
            return WordBlock16.Fold(seed, block.Slice(24));
        }
    }
}
