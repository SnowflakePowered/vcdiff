using System;
using System.Numerics;
using VCDiff.Shared;

#if NETCOREAPP3_1
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif
namespace VCDiff.Encoders
{
    internal class BlockHash
    {
        internal int blockSize = 16;

        private int maxMatchesToCheck;
        private const int maxProbes = 16;
        private long offset;
        private ulong hashTableMask;
        private long lastBlockAdded;
        private long[] hashTable;
        private long[] nextBlockTable;
        private long[] lastBlockTable;
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
        public BlockHash(ByteBuffer sin, int offset, RollingHash hasher, int blockSize = 16)
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
            hashTable = new long[tableSize];
            nextBlockTable = new long[blocksCount];
            lastBlockTable = new long[blocksCount];
            lastBlockAdded = -1;
            SetTablesToInvalid();
        }

        private void SetTablesToInvalid()
        {
            Array.Fill(lastBlockTable, -1);
            Array.Fill(nextBlockTable, -1);
            Array.Fill(hashTable, -1);
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
            source.Position = offset;
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

        public long TableSize => tableSize;

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
        /// <param name="target">the target buffer</param>
        /// <param name="m">the match object to use</param>
        public unsafe void FindBestMatch(ulong hash, long candidateStart, long targetStart, long targetSize, byte* targetPtr, ByteBuffer target, ref Match m)
        {
            int matchCounter = 0;

            for (long blockNumber = FirstMatchingBlock(hash, candidateStart, sourcePtr, targetPtr, target);
                blockNumber >= 0 && !TooManyMatches(ref matchCounter);
                blockNumber = NextMatchingBlock(blockNumber, candidateStart, sourcePtr, targetPtr, target))
            {
                long sourceMatchOffset = blockNumber * blockSize;
                long sourceStart = blockNumber * blockSize;
                long sourceMatchEnd = sourceMatchOffset + blockSize;
                long targetMatchOffset = candidateStart - targetStart;
                long targetMatchEnd = targetMatchOffset + blockSize;

                long matchSize = blockSize;

                long limitBytesToLeft = Math.Min(sourceMatchOffset, targetMatchOffset);
                long leftMatching = MatchingBytesToLeft(sourceMatchOffset, targetStart + targetMatchOffset, sourcePtr, 
                    targetPtr, limitBytesToLeft);
                sourceMatchOffset -= leftMatching;
                targetMatchOffset -= leftMatching;
                matchSize += leftMatching;

                long sourceBytesToRight = source.Length - sourceMatchEnd;
                long targetBytesToRight = targetSize - targetMatchEnd;
                long rightLimit = Math.Min(sourceBytesToRight, targetBytesToRight);

                long rightMatching = MatchingBytesToRight(sourceMatchEnd, targetStart + targetMatchEnd, sourcePtr, targetPtr, 
                    target, rightLimit);
                matchSize += rightMatching;
                //sourceMatchEnd += rightMatching;
                //targetMatchEnd += rightMatching;
                m.ReplaceIfBetterMatch(matchSize, sourceMatchOffset + offset, targetMatchOffset);
            }
        }

        public void AddBlock(ulong hash)
        {
            long blockNumber = lastBlockAdded + 1;
            long totalBlocks = blocksCount;
            if (blockNumber >= totalBlocks)
            {
                return;
            }

            if (nextBlockTable[blockNumber] != -1)
            {
                return;
            }

            long tableIndex = GetTableIndex(hash);
            long firstMatching = hashTable[tableIndex];
            if (firstMatching < 0)
            {
                hashTable[tableIndex] = blockNumber;
                lastBlockTable[blockNumber] = blockNumber;
            }
            else
            {
                long lastMatching = lastBlockTable[firstMatching];
                if (nextBlockTable[lastMatching] != -1)
                {
                    return;
                }
                nextBlockTable[lastMatching] = blockNumber;
                lastBlockTable[firstMatching] = blockNumber;
            }
            lastBlockAdded = blockNumber;
        }

        public void AddAllBlocks()
        {
            AddAllBlocksThroughIndex(source.Length);
        }

        private unsafe bool BlockContentsMatch(long block1, long tOffset, byte *sourcePtr, byte *targetPtr, ByteBuffer target)
        {
#if NETCOREAPP3_1
            long tLen = target.Length;
            if (Avx2.IsSupported && blockSize % 32 == 0)
            {
                byte* sPtr = sourcePtr + block1 * blockSize;
                byte* tPtr = targetPtr + tOffset;

                if (block1 * blockSize > source.Length || tOffset > tLen) return false;

                for (int i = 0; i < blockSize; i += 16)
                {
                    Vector256<byte> lv = Avx2.LoadDquVector256(&sPtr[i]);
                    Vector256<byte> rv = Avx2.LoadDquVector256(&tPtr[i]);
                    if (Avx2.MoveMask(Avx2.CompareEqual(lv, rv)) != EQMASK) return false;
                }
                return true;
            }

            if (Sse2.IsSupported && blockSize % 16 == 0)
            {
                byte* sPtr = sourcePtr + block1 * blockSize;
                byte* tPtr = targetPtr + tOffset;

                if (block1 * blockSize > source.Length || tOffset > tLen) return false;

                for (int i = 0; i < blockSize; i += 16)
                {
                    Vector128<byte> lv = Sse2.LoadVector128(&sPtr[i]);
                    Vector128<byte> rv = Sse2.LoadVector128(&tPtr[i]);
                    if ((uint) Sse2.MoveMask(Sse2.CompareEqual(lv, rv)) != ushort.MaxValue) return false;
                }
                return true;
            }
#endif
            //this sets up the positioning of the buffers
            //as well as testing the first byte
            source.Position = block1 * blockSize;
            target.Position = tOffset;

            if (!source.CanRead || !target.CanRead) return false;
            var s1 = source.PeekBytes(blockSize).Span;
            var s2 = target.PeekBytes(blockSize).Span;

            return s1.SequenceCompareTo(s2) == 0;
        }

        private unsafe long FirstMatchingBlock(ulong hash, long toffset, byte* sourcePtr, byte* targetPtr, ByteBuffer target)
        {
            return SkipNonMatchingBlocks(hashTable[GetTableIndex(hash)], toffset, sourcePtr, targetPtr, target);
        }

        private unsafe long NextMatchingBlock(long blockNumber, long toffset, byte* sourcePtr, byte* targetPtr, ByteBuffer target)
        {
            if (blockNumber >= blocksCount)
            {
                return -1;
            }

            return SkipNonMatchingBlocks(nextBlockTable[blockNumber], toffset, sourcePtr, targetPtr, target);
        }

        private unsafe long SkipNonMatchingBlocks(long blockNumber, long toffset, byte* sourcePtr, byte* targetPtr, ByteBuffer target)
        {
            int probes = 0;
            while ((blockNumber >= 0) && !BlockContentsMatch(blockNumber, toffset, sourcePtr, targetPtr, target))
            {
                if (++probes > maxProbes)
                {
                    return -1;
                }
                blockNumber = nextBlockTable[blockNumber];
            }
            return blockNumber;
        }

#if NETCOREAPP3_1
        private unsafe long MatchingBytesToLeftAvx2(long start, long tstart, byte* sourcePtr, byte* targetPtr, long maxBytes)
        {
            long sindex = start;
            long tindex = tstart;
            long bytesFound = 0;
            byte* tPtr = targetPtr;
            byte* sPtr = sourcePtr;

            for (; (sindex >= 32 && tindex >= 32) && bytesFound <= maxBytes - 32; bytesFound += 32)
            {
                tindex -= 32;
                sindex -= 32;
                var lv = Avx2.LoadDquVector256(&sPtr[sindex]);
                var rv = Avx2.LoadDquVector256(&tPtr[tindex]);
                if (Avx2.MoveMask(Avx2.CompareEqual(lv, rv)) == EQMASK) continue;
                tindex += 32;
                sindex += 32;
                break;
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

        private unsafe long MatchingBytesToLeftSse2(long start, long tstart, byte* sourcePtr, byte* targetPtr, long maxBytes)
        {
            long sindex = start;
            long tindex = tstart;
            long bytesFound = 0;
            byte* tPtr = targetPtr;
            byte* sPtr = sourcePtr;

            for (; (sindex >= 16 && tindex >= 16) && bytesFound <= maxBytes - 16; bytesFound += 16)
            {
                tindex -= 16;
                sindex -= 16;
                var lv = Sse2.LoadVector128(&sPtr[sindex]);
                var rv = Sse2.LoadVector128(&tPtr[tindex]);
                if ((uint)Sse2.MoveMask(Sse2.CompareEqual(lv, rv)) == ushort.MaxValue) continue;
                tindex += 16;
                sindex += 16;
                break;
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
        private unsafe long MatchingBytesToLeft(long start, long tstart, byte* sourcePtr, byte* targetPtr, long maxBytes)
        {
#if NETCOREAPP3_1
            if (Avx2.IsSupported) return MatchingBytesToLeftAvx2(start, tstart, sourcePtr, targetPtr, maxBytes);
            if (Sse2.IsSupported) return MatchingBytesToLeftSse2(start, tstart, sourcePtr, targetPtr, maxBytes);
#endif
            long bytesFound = 0;
            long sindex = start;
            long tindex = tstart;
            byte* tPtr = targetPtr;
            byte* sPtr = sourcePtr;

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

#if NETCOREAPP3_1
        private unsafe long MatchingBytesToRightAvx2(long end, long tstart, byte* sourcePtr, byte* targetPtr, ByteBuffer target, long maxBytes)
        {
            long sindex = end;
            long tindex = tstart;
            long bytesFound = 0;
            long srcLength = source.Length;
            long trgLength = target.Length;
            byte* tPtr = targetPtr;
            byte* sPtr = sourcePtr;

            for (; (srcLength - sindex) >= 32 && (trgLength - tindex) >= 32 && bytesFound <= maxBytes - 32; bytesFound += 32, tindex += 32, sindex += 32)
            {
                var lv = Avx2.LoadDquVector256(&sPtr[sindex]);
                var rv = Avx2.LoadDquVector256(&tPtr[tindex]);
                if (Avx2.MoveMask(Avx2.CompareEqual(lv, rv)) == EQMASK) continue;
                break;
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

        private unsafe long MatchingBytesToRightSse2(long end, long tstart, byte* sourcePtr, byte* targetPtr, ByteBuffer target, long maxBytes)
        {
            long sindex = end;
            long tindex = tstart;
            long bytesFound = 0;
            long srcLength = source.Length;
            long trgLength = target.Length;
            byte* tPtr = targetPtr;
            byte* sPtr = sourcePtr;

            for (; (srcLength - sindex) >= 16 && (trgLength - tindex) >= 16 && bytesFound <= maxBytes - 16; bytesFound += 16, tindex += 16, sindex += 32)
            {
                var lv = Sse2.LoadVector128(&sPtr[sindex]);
                var rv = Sse2.LoadVector128(&tPtr[tindex]);
                if (Sse2.MoveMask(Sse2.CompareEqual(lv, rv)) == ushort.MaxValue) continue;
                break;
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
        private unsafe long MatchingBytesToRight(long end, long tstart, byte* sourcePtr, byte* targetPtr, ByteBuffer target, long maxBytes)
        {

#if NETCOREAPP3_1
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
            private long size;
            private long sOffset;
            private long tOffset;

            public void ReplaceIfBetterMatch(long csize, long sourcOffset, long targetOffset)
            {
                if (csize <= size) return;
                size = csize;
                sOffset = sourcOffset;
                tOffset = targetOffset;
            }

            public long Size => size;

            public long SourceOffset => sOffset;

            public long TargetOffset => tOffset;
        }
    }
}