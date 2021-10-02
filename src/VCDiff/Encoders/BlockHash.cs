using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using VCDiff.Shared;

#if NETCOREAPP3_1 || NET5_0
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif
namespace VCDiff.Encoders
{
    internal class BlockHash : IDisposable
    {
        internal int blockSize = 16;

        private int maxMatchesToCheck;
        private const int maxProbes = 16;
        private long offset;
        private ulong hashTableMask;
        private long lastBlockAdded;
        private NativeAllocation<long> hashTable;
        private NativeAllocation<long> nextBlockTable;
        private NativeAllocation<long> lastBlockTable;
        private long tableSize;
        private RollingHash hasher;
        private readonly ByteBuffer source;
        private unsafe byte* sourcePtr;
        private const int EQMASK = unchecked((int)(0b1111_1111_1111_1111_1111_1111_1111_1111));

        /// <summary>
        /// Create a hash lookup table for the data
        /// </summary>
        /// <param name="sin">the data to create the table for</param>
        /// <param name="offset">the offset usually 0</param>
        /// <param name="hasher">the hashing method</param>
        /// <param name="blockSize">The block size to use</param>
        public unsafe BlockHash(ByteBuffer sin, int offset, RollingHash hasher, int blockSize = 16)
        {
            this.blockSize = blockSize;
            this.maxMatchesToCheck = (this.blockSize >= 32) ? 32 : (32 * (32 / this.blockSize));
            this.hasher = hasher;
            this.source = sin;
            this.offset = offset;
            unsafe
            {
                this.sourcePtr = source.DangerousGetBytePointer();
            }

            tableSize = CalcTableSize();

            if (tableSize == 0)
            {
                throw new Exception("BlockHash Table Size is Invalid == 0");
            }

            this.blocksCount = source.Length / blockSize;

            hashTableMask = (ulong)tableSize - 1;

            hashTable = new NativeAllocation<long>((int)tableSize);
            nextBlockTable = new NativeAllocation<long>((int)blocksCount);
            lastBlockTable = new NativeAllocation<long>((int)blocksCount);

            lastBlockAdded = -1;
            SetTablesToInvalid();
        }

        ~BlockHash()
        {
            Dispose();
        }

        private unsafe void SetTablesToInvalid()
        {
            Intrinsics.FillArrayVectorized(lastBlockTable.Pointer, lastBlockTable.NumItems, -1);
            Intrinsics.FillArrayVectorized(nextBlockTable.Pointer, nextBlockTable.NumItems, -1);
            Intrinsics.FillArrayVectorized(hashTable.Pointer, hashTable.NumItems, -1);
        }

        private long CalcTableSize()
        {
            long min = (this.source.Length / sizeof(int)) + 1;
            long size = 1;

            while (size < min)
            {
                size <<= 1;

                if (size <= 0)
                {
                    return 0;
                }
            }

            if ((size & (size - 1)) != 0)
            {
                return 0;
            }

            if ((source.Length > 0) && (size > (min * 2)))
            {
                return 0;
            }
            return size;
        }

        public void AddOneIndexHash(int index, ulong hash)
        {
            if (index == NextIndexToAdd)
            {
                AddBlock(hash);
            }
        }

        public long NextIndexToAdd => (lastBlockAdded + 1) * blockSize;

        public void AddAllBlocksThroughIndex(long index)
        {
            if (index > source.Length)
            {
                return;
            }

            long lastAdded = lastBlockAdded * blockSize;
            if (index <= lastAdded)
            {
                return;
            }

            if (source.Length < blockSize)
            {
                return;
            }

            long endLimit = index;
            long lastLegalHashIndex = (source.Length - blockSize);

            if (endLimit > lastLegalHashIndex)
            {
                endLimit = lastLegalHashIndex + 1;
            }

            long offset = source.Position + NextIndexToAdd;
            long end = source.Position + endLimit;
            source.Position = (int) offset;
            while (offset < end)
            {
                unsafe
                {
                    AddBlock(hasher.Hash(source.DangerousGetBytePointerAtCurrentPositionAndIncreaseOffsetAfter(blockSize), blockSize));
                }

                offset += blockSize;
            }
        }

        public long blocksCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long GetTableIndex(ulong hash)
        {
            return (long)(hash & hashTableMask);
        }

        /// <summary>
        /// Finds the best matching block for the candidate
        /// </summary>
        /// <param name="hash">the hash to look for</param>
        /// <param name="candidateStart">the start position</param>
        /// <param name="targetStart">the target start position</param>
        /// <param name="targetSize">the data left to encode</param>
        /// <param name="targetPtr">pointer to the target buffer</param>
        /// <param name="target">the target buffer</param>
        /// <param name="m">the match object to use</param>
#if NETCOREAPP3_1 || NET5_0
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#endif
#if NET5_0
        [SkipLocalsInit]
#endif
        public unsafe void FindBestMatch(ulong hash, long candidateStart, long targetStart, long targetSize, byte* targetPtr, ByteBuffer target, ref Match m)
        {
            int matchCounter = 0;

            for (long blockNumber = FirstMatchingBlock(hash, candidateStart, sourcePtr, targetPtr, target);
                blockNumber >= 0 && !TooManyMatches(ref matchCounter);
                blockNumber = NextMatchingBlock(blockNumber, candidateStart, sourcePtr, targetPtr, target))
            {
                long sourceMatchOffset = blockNumber * blockSize;
                long sourceMatchEnd = sourceMatchOffset + blockSize;
                long targetMatchOffset = candidateStart - targetStart;
                long targetMatchEnd = targetMatchOffset + blockSize;

                long matchSize = blockSize;

                long limitBytesToLeft = Math.Min(sourceMatchOffset, targetMatchOffset);
                long leftMatching = MatchingBytesToLeft(sourceMatchOffset, targetStart + targetMatchOffset, sourcePtr, 
                    targetPtr, target, limitBytesToLeft);
                sourceMatchOffset -= leftMatching;
                targetMatchOffset -= leftMatching;
                matchSize += leftMatching;

                long sourceBytesToRight = source.Length - sourceMatchEnd;
                long targetBytesToRight = targetSize - targetMatchEnd;
                long rightLimit = Math.Min(sourceBytesToRight, targetBytesToRight);

                long rightMatching = MatchingBytesToRight(sourceMatchEnd, targetStart + targetMatchEnd, sourcePtr, targetPtr, target, rightLimit);
                matchSize += rightMatching;
                m.ReplaceIfBetterMatch(matchSize, sourceMatchOffset + offset, targetMatchOffset);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void AddBlock(ulong hash)
        {
            long blockNumber = lastBlockAdded + 1;
            long totalBlocks = blocksCount;
            if (blockNumber >= totalBlocks)
            {
                return;
            }

            if (nextBlockTable.Pointer[blockNumber] != -1)
            {
                return;
            }

            long tableIndex = GetTableIndex(hash);
            long firstMatching = hashTable.Pointer[tableIndex];
            if (firstMatching < 0)
            {
                hashTable.Pointer[tableIndex] = blockNumber;
                lastBlockTable.Pointer[blockNumber] = blockNumber;
            }
            else
            {
                long lastMatching = lastBlockTable.Pointer[firstMatching];
                if (nextBlockTable.Pointer[lastMatching] != -1)
                {
                    return;
                }
                nextBlockTable.Pointer[lastMatching] = blockNumber;
                lastBlockTable.Pointer[firstMatching] = blockNumber;
            }
            lastBlockAdded = blockNumber;
        }

        public void AddAllBlocks()
        {
            AddAllBlocksThroughIndex(source.Length);
        }

#if NET5_0
        [SkipLocalsInit]
#endif
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe bool BlockContentsMatch(long block1, long tOffset, byte *sourcePtr, byte *targetPtr, ByteBuffer target)
        {
            int lengthToExamine = blockSize;
            int sOffset = (int)(block1 * blockSize);
            long sLen = source.Length;
            long tLen = target.Length;
            byte* sPtr = sourcePtr;
            byte* tPtr = targetPtr;

            if (sOffset > sLen || tOffset > tLen)
                return false;

#if NETCOREAPP3_1 || NET5_0
            if (Avx2.IsSupported && lengthToExamine >= Intrinsics.AvxRegisterSize)
            {
                if (sOffset >= Intrinsics.AvxRegisterSize && tOffset >= Intrinsics.AvxRegisterSize)
                {
                    while (lengthToExamine >= Intrinsics.AvxRegisterSize)
                    {
                        Vector256<byte> lv = Avx.LoadVector256(&sPtr[sOffset]);
                        Vector256<byte> rv = Avx.LoadVector256(&tPtr[tOffset]);
                        if (Avx2.MoveMask(Avx2.CompareEqual(lv, rv)) != EQMASK)
                            return false;

                        sOffset += Intrinsics.AvxRegisterSize;
                        tOffset += Intrinsics.AvxRegisterSize;
                        lengthToExamine -= Intrinsics.AvxRegisterSize;
                    }
                }
            }

            if (Sse2.IsSupported && lengthToExamine >= Intrinsics.SseRegisterSize)
            {
                if (sOffset >= Intrinsics.SseRegisterSize && tOffset >= Intrinsics.SseRegisterSize)
                {
                    while (lengthToExamine >= Intrinsics.SseRegisterSize)
                    {
                        Vector128<byte> lv = Sse2.LoadVector128(&sPtr[sOffset]);
                        Vector128<byte> rv = Sse2.LoadVector128(&tPtr[tOffset]);
                        if ((uint)Sse2.MoveMask(Sse2.CompareEqual(lv, rv)) != ushort.MaxValue)
                            return false;

                        sOffset += Intrinsics.SseRegisterSize;
                        tOffset += Intrinsics.SseRegisterSize;
                        lengthToExamine -= Intrinsics.SseRegisterSize;
                    }
                }
            }
#else
            int vectorSize = Vector<byte>.Count;
            if (lengthToExamine >= vectorSize)
            {
                var sBuf = source.AsSpan();
                var tBuf = target.AsSpan();
                
                if (sOffset >= vectorSize && tOffset >= vectorSize)
                {
                    while (lengthToExamine >= vectorSize)
                    {
                        Vector<byte> lv = new Vector<byte>(sBuf.Slice(sOffset));
                        Vector<byte> rv = new Vector<byte>(tBuf.Slice((int)tOffset));
                        if (!Vector.EqualsAll(lv, rv))
                            return false;

                        sOffset += vectorSize;
                        tOffset += vectorSize;
                        lengthToExamine -= vectorSize;
                    }
                }
            }
#endif

            while (lengthToExamine > 0 && !(sOffset > sLen || tOffset > tLen))
            {
                if (sPtr[sOffset] != tPtr[tOffset]) 
                    return false;

                --lengthToExamine;
                ++sOffset;
                ++tOffset;
            }

            return true;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe long FirstMatchingBlock(ulong hash, long toffset, byte* sourcePtr, byte* targetPtr, ByteBuffer target)
        {
            return SkipNonMatchingBlocks(hashTable.Pointer[GetTableIndex(hash)], toffset, sourcePtr, targetPtr, target);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe long NextMatchingBlock(long blockNumber, long toffset, byte* sourcePtr, byte* targetPtr, ByteBuffer target)
        {
            if (blockNumber >= blocksCount)
            {
                return -1;
            }

            return SkipNonMatchingBlocks(nextBlockTable.Pointer[blockNumber], toffset, sourcePtr, targetPtr, target);
        }

#if NET5_0
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        [SkipLocalsInit]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private unsafe long SkipNonMatchingBlocks(long blockNumber, long toffset, byte* sourcePtr, byte* targetPtr, ByteBuffer target)
        {
            int probes = 0;
            var ptr = nextBlockTable.Pointer;
            while ((blockNumber >= 0) && !BlockContentsMatch(blockNumber, toffset, sourcePtr, targetPtr, target))
            {
                if (++probes > maxProbes)
                {
                    return -1;
                }
                blockNumber = ptr[blockNumber];
            }
            return blockNumber;
        }

#if NETCOREAPP3_1 || NET5_0
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe long MatchingBytesToLeftAvx2(long start, long tstart, byte* sourcePtr, byte* targetPtr, long maxBytes)
        {
            long sindex = start;
            long tindex = tstart;
            long bytesFound = 0;
            byte* tPtr = targetPtr;
            byte* sPtr = sourcePtr;

            if (sindex >= Intrinsics.AvxRegisterSize && tindex >= Intrinsics.AvxRegisterSize)
            {
                while (bytesFound <= maxBytes - Intrinsics.AvxRegisterSize)
                {
                    tindex -= Intrinsics.AvxRegisterSize;
                    sindex -= Intrinsics.AvxRegisterSize;
                    var lv = Avx2.LoadVector256(&sPtr[sindex]);
                    var rv = Avx2.LoadVector256(&tPtr[tindex]);
                    if (Avx2.MoveMask(Avx2.CompareEqual(lv, rv)) != EQMASK)
                    {
                        tindex += Intrinsics.AvxRegisterSize;
                        sindex += Intrinsics.AvxRegisterSize;
                        break;
                    }

                    bytesFound += Intrinsics.AvxRegisterSize;
                }
            }

            while (bytesFound < maxBytes)
            {
                --sindex;
                --tindex;
                if (sindex < 0 || tindex < 0) break;
                // has to be done this way or a race condition will happen
                // if the source and target are the same buffer
                byte lb = sPtr[sindex];
                byte rb = tPtr[tindex];
                if (lb != rb) break;
               
                ++bytesFound;
            }

            return bytesFound;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe long MatchingBytesToLeftSse2(long start, long tstart, byte* sourcePtr, byte* targetPtr, long maxBytes)
        {
            long sindex = start;
            long tindex = tstart;
            long bytesFound = 0;
            byte* tPtr = targetPtr;
            byte* sPtr = sourcePtr;

            if (sindex >= Intrinsics.SseRegisterSize && tindex >= Intrinsics.SseRegisterSize)
            {
                while (bytesFound <= maxBytes - Intrinsics.SseRegisterSize)
                {
                    tindex -= Intrinsics.SseRegisterSize;
                    sindex -= Intrinsics.SseRegisterSize;
                    var lv = Sse2.LoadVector128(&sPtr[sindex]);
                    var rv = Sse2.LoadVector128(&tPtr[tindex]);
                    if ((uint)Sse2.MoveMask(Sse2.CompareEqual(lv, rv)) != ushort.MaxValue)
                    {
                        tindex += Intrinsics.SseRegisterSize;
                        sindex += Intrinsics.SseRegisterSize;
                        break;
                    }

                    bytesFound += Intrinsics.SseRegisterSize;
                }
            }
            
            while (bytesFound < maxBytes)
            {
                --sindex;
                --tindex;
                if (sindex < 0 || tindex < 0) break;
                // has to be done this way or a race condition will happen
                // if the source and target are the same buffer
                byte lb = sPtr[sindex];
                byte rb = tPtr[tindex];
                if (lb != rb) break;

                ++bytesFound;
            }

            return bytesFound;
        }
#endif

#if NET5_0
        [SkipLocalsInit]
#endif
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe long MatchingBytesToLeft(long start, long tstart, byte* sourcePtr, byte* targetPtr, ByteBuffer target, long maxBytes)
        {
#if NETCOREAPP3_1 || NET5_0
            if (Avx2.IsSupported) return MatchingBytesToLeftAvx2(start, tstart, sourcePtr, targetPtr, maxBytes);
            if (Sse2.IsSupported) return MatchingBytesToLeftSse2(start, tstart, sourcePtr, targetPtr, maxBytes);
#endif
            long bytesFound = 0;
            long sindex = start;
            long tindex = tstart;
            byte* tPtr = targetPtr;
            byte* sPtr = sourcePtr;

            int vectorSize = Vector<byte>.Count;
            var tBuf = target.AsSpan();
            var sBuf = source.AsSpan();

            while (sindex >= vectorSize && tindex >= vectorSize && bytesFound <= maxBytes - vectorSize)
            {
                tindex -= vectorSize;
                sindex -= vectorSize;
                var lv = new Vector<byte>(sBuf.Slice((int)sindex));
                var rv = new Vector<byte>(tBuf.Slice((int)tindex));
                if (!Vector.EqualsAll(lv, rv))
                {
                    tindex += vectorSize;
                    sindex += vectorSize;
                    break;
                }

                bytesFound += vectorSize;
            }

            while (bytesFound < maxBytes)
            {
                --sindex;
                --tindex;
                if (sindex < 0 || tindex < 0) break;
                byte lb = sPtr[sindex];
                byte rb = tPtr[tindex];
                if (lb != rb) break;

                ++bytesFound;
            }

            return bytesFound;
        }

#if NETCOREAPP3_1 || NET5_0
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe long MatchingBytesToRightAvx2(long end, long tstart, byte* sourcePtr, byte* targetPtr, ByteBuffer target, long maxBytes)
        {
            long sindex = end;
            long tindex = tstart;
            long bytesFound = 0;
            long srcLength = source.Length;
            long trgLength = target.Length;
            byte* tPtr = targetPtr;
            byte* sPtr = sourcePtr;

            while ((srcLength - sindex) >= Intrinsics.AvxRegisterSize && 
                   (trgLength - tindex) >= Intrinsics.AvxRegisterSize && 
                   bytesFound <= maxBytes - Intrinsics.AvxRegisterSize)
            {
                var lv = Avx2.LoadVector256(&sPtr[sindex]);
                var rv = Avx2.LoadVector256(&tPtr[tindex]);
                if (Avx2.MoveMask(Avx2.CompareEqual(lv, rv)) != EQMASK)
                    break;

                bytesFound += Intrinsics.AvxRegisterSize;
                tindex += Intrinsics.AvxRegisterSize;
                sindex += Intrinsics.AvxRegisterSize;
            }

            while (bytesFound < maxBytes)
            {
                if (sindex >= srcLength || tindex >= trgLength) break;
                byte lb = sPtr[sindex];
                byte rb = tPtr[tindex];
                if (lb != rb) break;
                ++tindex;
                ++sindex;
                ++bytesFound;
            }
            return bytesFound;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe long MatchingBytesToRightSse2(long end, long tstart, byte* sourcePtr, byte* targetPtr, ByteBuffer target, long maxBytes)
        {
            long sindex = end;
            long tindex = tstart;
            long bytesFound = 0;
            long srcLength = source.Length;
            long trgLength = target.Length;
            byte* tPtr = targetPtr;
            byte* sPtr = sourcePtr;

            while ((srcLength - sindex) >= Intrinsics.SseRegisterSize && 
                   (trgLength - tindex) >= Intrinsics.SseRegisterSize && 
                   bytesFound <= maxBytes - Intrinsics.SseRegisterSize)
            {
                var lv = Sse2.LoadVector128(&sPtr[sindex]);
                var rv = Sse2.LoadVector128(&tPtr[tindex]);
                if (Sse2.MoveMask(Sse2.CompareEqual(lv, rv)) != ushort.MaxValue)
                    break;

                bytesFound += Intrinsics.SseRegisterSize;
                tindex += Intrinsics.SseRegisterSize;
                sindex += Intrinsics.SseRegisterSize;
            }
            
            while (bytesFound < maxBytes)
            {
                if (sindex >= srcLength || tindex >= trgLength) break;
                byte lb = sPtr[sindex];
                byte rb = tPtr[tindex];
                if (lb != rb) break;
                ++tindex;
                ++sindex;
                ++bytesFound;
            }

            return bytesFound;
        }
#endif

#if NET5_0
        [SkipLocalsInit]
#endif
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe long MatchingBytesToRight(long end, long tstart, byte* sourcePtr, byte* targetPtr, ByteBuffer target, long maxBytes)
        {

#if NETCOREAPP3_1 || NET5_0
            // ByteBuffer is already pinned, so its safe to just use raw pointer access
            // but Vector<T> can only create vectors from a Span and not an address. 
            // We can probably unroll the while loop in the scalar implementation
            // But this won't save us too much time.
            // Runtime.Intrinsics however has access to SSE and AVX functions that allow us to load a vector straight
            // from an address.
            if (Avx2.IsSupported) return MatchingBytesToRightAvx2(end, tstart, sourcePtr, targetPtr, target, maxBytes);
            if (Sse2.IsSupported) return MatchingBytesToRightSse2(end, tstart, sourcePtr, targetPtr, target, maxBytes);
#endif
            long sindex = end;
            long tindex = tstart;
            long bytesFound = 0;
            long srcLength = source.Length;
            long trgLength = target.Length;
            byte* tPtr = targetPtr;
            byte* sPtr = sourcePtr;
            int vectorSize = Vector<byte>.Count;
            var tBuf = target.AsSpan();
            var sBuf = source.AsSpan();

            while ((srcLength - sindex) >= vectorSize 
                   && (trgLength - tindex) >= vectorSize
                   && bytesFound <= maxBytes - vectorSize)
            {
                var lv = new Vector<byte>(sBuf.Slice((int)sindex));
                var rv = new Vector<byte>(tBuf.Slice((int)tindex));
                if (!Vector.EqualsAll(lv, rv))
                    break;

                bytesFound += vectorSize;
                tindex += vectorSize;
                sindex += vectorSize;
            }

            while (bytesFound < maxBytes)
            {
                if (sindex >= srcLength || tindex >= trgLength) break;
                byte lb = sPtr[sindex];
                byte rb = tPtr[tindex];
                if (lb != rb) break;
                ++tindex;
                ++sindex;
                ++bytesFound;
            }

            return bytesFound;
        }

        public bool TooManyMatches(ref int matchCounter)
        {
            ++matchCounter;
            return (matchCounter > maxMatchesToCheck);
        }

        public ref struct Match
        {
            public long size;
            public long sOffset;
            public long tOffset;

            public void ReplaceIfBetterMatch(long csize, long sourcOffset, long targetOffset)
            {
                if (csize <= size) return;
                size = csize;
                sOffset = sourcOffset;
                tOffset = targetOffset;
            }
        }

        public void Dispose()
        {
            hashTable.Dispose();
            nextBlockTable.Dispose();
            lastBlockTable.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}