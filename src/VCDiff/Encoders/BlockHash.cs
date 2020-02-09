using System;
using VCDiff.Shared;

namespace VCDiff.Encoders
{
    internal class BlockHash
    {
        private static int kBlockSize = 16;

        public static int BlockSize
        {
            get => kBlockSize;
            set
            {
                if (value < 2) return;
                kBlockSize = value;
            }
        }

        private static int maxMatchesToCheck = (kBlockSize >= 32) ? 32 : (32 * (32 / kBlockSize));
        private const int maxProbes = 16;
        private long offset;
        private ulong hashTableMask;
        private long lastBlockAdded;
        private long[] hashTable;
        private long[] nextBlockTable;
        private long[] lastBlockTable;
        private long tableSize;
        private RollingHash hasher;

        public IByteBuffer Source { get; }

        /// <summary>
        /// Create a hash lookup table for the data
        /// </summary>
        /// <param name="sin">the data to create the table for</param>
        /// <param name="offset">the offset usually 0</param>
        /// <param name="hasher">the hashing method</param>
        public BlockHash(IByteBuffer sin, int offset, RollingHash hasher)
        {
            maxMatchesToCheck = (kBlockSize >= 32) ? 32 : (32 * (32 / kBlockSize));
            this.hasher = hasher;
            this.Source = sin;
            this.offset = offset;
            tableSize = CalcTableSize();

            if (tableSize == 0)
            {
                throw new Exception("BlockHash Table Size is Invalid == 0");
            }

            hashTableMask = (ulong)tableSize - 1;
            hashTable = new long[tableSize];
            nextBlockTable = new long[BlocksCount];
            lastBlockTable = new long[BlocksCount];
            lastBlockAdded = -1;
            SetTablesToInvalid();
        }

        private void SetTablesToInvalid()
        {
            for (int i = 0; i < nextBlockTable.Length; i++)
            {
                lastBlockTable[i] = -1;
                nextBlockTable[i] = -1;
            }
            for (int i = 0; i < hashTable.Length; i++)
            {
                hashTable[i] = -1;
            }
        }

        private long CalcTableSize()
        {
            long min = (this.Source.Length / sizeof(int)) + 1;
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

            if ((Source.Length > 0) && (size > (min * 2)))
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

        public long NextIndexToAdd => (lastBlockAdded + 1) * kBlockSize;

        public void AddAllBlocksThroughIndex(long index)
        {
            if (index > Source.Length)
            {
                return;
            }

            long lastAdded = lastBlockAdded * kBlockSize;
            if (index <= lastAdded)
            {
                return;
            }

            if (Source.Length < kBlockSize)
            {
                return;
            }

            long endLimit = index;
            long lastLegalHashIndex = (Source.Length - kBlockSize);

            if (endLimit > lastLegalHashIndex)
            {
                endLimit = lastLegalHashIndex + 1;
            }

            long offset = Source.Position + NextIndexToAdd;
            long end = Source.Position + endLimit;
            Source.Position = offset;
            while (offset < end)
            {
                AddBlock(hasher.Hash(Source.ReadBytes(kBlockSize)));
                offset += kBlockSize;
            }
        }

        public long BlocksCount
        {
            get
            {
                return Source.Length / kBlockSize;
            }
        }

        public long TableSize
        {
            get
            {
                return tableSize;
            }
        }

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
        public void FindBestMatch(ulong hash, long candidateStart, long targetStart, long targetSize, IByteBuffer target, ref Match m)
        {
            int matchCounter = 0;

            for (long blockNumber = FirstMatchingBlock(hash, candidateStart, target);
                blockNumber >= 0 && !TooManyMatches(ref matchCounter);
                blockNumber = NextMatchingBlock(blockNumber, candidateStart, target))
            {
                long sourceMatchOffset = blockNumber * kBlockSize;
                long sourceStart = blockNumber * kBlockSize;
                long sourceMatchEnd = sourceMatchOffset + kBlockSize;
                long targetMatchOffset = candidateStart - targetStart;
                long targetMatchEnd = targetMatchOffset + kBlockSize;

                long matchSize = kBlockSize;

                long limitBytesToLeft = Math.Min(sourceMatchOffset, targetMatchOffset);
                long leftMatching = MatchingBytesToLeft(sourceMatchOffset, targetStart + targetMatchOffset, target, limitBytesToLeft);
                sourceMatchOffset -= leftMatching;
                targetMatchOffset -= leftMatching;
                matchSize += leftMatching;

                long sourceBytesToRight = Source.Length - sourceMatchEnd;
                long targetBytesToRight = targetSize - targetMatchEnd;
                long rightLimit = Math.Min(sourceBytesToRight, targetBytesToRight);

                long rightMatching = MatchingBytesToRight(sourceMatchEnd, targetStart + targetMatchEnd, target, rightLimit);
                matchSize += rightMatching;
                sourceMatchEnd += rightMatching;
                targetMatchEnd += rightMatching;
                m.ReplaceIfBetterMatch(matchSize, sourceMatchOffset + offset, targetMatchOffset);
            }
        }

        public void AddBlock(ulong hash)
        {
            long blockNumber = lastBlockAdded + 1;
            long totalBlocks = BlocksCount;
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
            AddAllBlocksThroughIndex(Source.Length);
        }

        public bool BlockContentsMatch(long block1, long toffset, IByteBuffer target)
        {
            //this sets up the positioning of the buffers
            //as well as testing the first byte
            this.Source.Position = block1 * kBlockSize;
            if (!this.Source.CanRead) return false;
            byte lb = this.Source.ReadByte();
            target.Position = toffset;
            if (!target.CanRead) return false;
            byte rb = target.ReadByte();

            return lb == rb && BlockCompareWords(target);
        }

        //this doesn't appear to be used anywhere even though it is included in googles code
        public bool BlockCompareWords(IByteBuffer target)
        {
            var block1 = this.Source.PeekBytes(kBlockSize).Span;
            var block2 = target.PeekBytes(kBlockSize).Span;

            return block1.SequenceCompareTo(block2) == 0;
        }

        public long FirstMatchingBlock(ulong hash, long toffset, IByteBuffer target)
        {
            return SkipNonMatchingBlocks(hashTable[GetTableIndex(hash)], toffset, target);
        }

        public long NextMatchingBlock(long blockNumber, long toffset, IByteBuffer target)
        {
            if (blockNumber >= BlocksCount)
            {
                return -1;
            }

            return SkipNonMatchingBlocks(nextBlockTable[blockNumber], toffset, target);
        }

        public long SkipNonMatchingBlocks(long blockNumber, long toffset, IByteBuffer target)
        {
            int probes = 0;
            while ((blockNumber >= 0) && !BlockContentsMatch(blockNumber, toffset, target))
            {
                if (++probes > maxProbes)
                {
                    return -1;
                }
                blockNumber = nextBlockTable[blockNumber];
            }
            return blockNumber;
        }

        public long MatchingBytesToLeft(long start, long tstart, IByteBuffer target, long maxBytes)
        {
            long bytesFound = 0;
            long sindex = start;
            long tindex = tstart;

            while (bytesFound < maxBytes)
            {
                --sindex;
                --tindex;
                if (sindex < 0 || tindex < 0) break;
                //has to be done this way or a race condition will happen
                //if the sourcce and target are the same buffer
                Source.Position = sindex;
                byte lb = Source.ReadByte();
                target.Position = tindex;
                byte rb = target.ReadByte();
                if (lb != rb) break;
                ++bytesFound;
            }
            return bytesFound;
        }

        public long MatchingBytesToRight(long end, long tstart, IByteBuffer target, long maxBytes)
        {
            long sindex = end;
            long tindex = tstart;
            long bytesFound = 0;
            long srcLength = Source.Length;
            long trgLength = target.Length;
            Source.Position = end;
            target.Position = tstart;
            while (bytesFound < maxBytes)
            {
                if (sindex >= srcLength || tindex >= trgLength) break;
                if (!Source.CanRead) break;
                byte lb = Source.ReadByte();
                if (!target.CanRead) break;
                byte rb = target.ReadByte();
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