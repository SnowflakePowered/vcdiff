using System;
using VCDiff.Shared;

namespace VCDiff.Encoders
{
    internal class ChunkEncoder : IDisposable
    {
        private static int minBlockSize = 32;

        public static int MinBlockSize
        {
            get
            {
                return minBlockSize;
            }
            set
            {
                if (value < 2 || value < BlockHash.BlockSize) return;
                minBlockSize = value;
            }
        }

        private BlockHash dictionary;
        private IByteBuffer oldData;
        private IByteBuffer newData;
        private WindowEncoder windowEncoder;
        private RollingHash hasher;
        private bool interleaved;
        private bool hasChecksum;

        /// <summary>
        /// Performs the actual encoding of a chunk of data into the VCDiff format
        /// </summary>
        /// <param name="dictionary">The dictionary hash table</param>
        /// <param name="oldData">The data for the dictionary hash table</param>
        /// <param name="hash">The rolling hash object</param>
        /// <param name="interleaved">Whether to interleave the data or not</param>
        /// <param name="checksum">Whether to include checksums for each window</param>
        public ChunkEncoder(BlockHash dictionary, IByteBuffer oldData, RollingHash hash, bool interleaved = false, bool checksum = false)
        {
            this.hasChecksum = checksum;
            this.hasher = hash;
            this.oldData = oldData;
            this.dictionary = dictionary;
            this.interleaved = interleaved;
        }

        /// <summary>
        /// Encodes the data using the settings from initialization
        /// </summary>
        /// <param name="newData">the target data</param>
        /// <param name="sout">the out stream</param>
        public void EncodeChunk(IByteBuffer newData, ByteStreamWriter sout)
        {
            uint checksum = 0;

            ///If checksum needed
            ///Generate Adler32 checksum for the incoming bytes
            if (hasChecksum)
            {
                newData.Position = 0;
                ReadOnlyMemory<byte> bytes = newData.ReadBytes((int)newData.Length);
                checksum = Checksum.ComputeAdler32(bytes);
            }

            windowEncoder = new WindowEncoder(oldData.Length, checksum, this.interleaved, hasChecksum);

            oldData.Position = 0;
            newData.Position = 0;

            this.newData = newData;
            long nextEncode = newData.Position;
            long targetEnd = newData.Length;
            long startOfLastBlock = targetEnd - BlockHash.BlockSize;
            long candidatePos = nextEncode;

            //create the first hash
            ulong hash = hasher.Hash(newData.PeekBytes(BlockHash.BlockSize));

            while (true)
            {
                //if less than block size exit and then write as an ADD
                if (newData.Length - nextEncode < BlockHash.BlockSize)
                {
                    break;
                }

                //try and encode the copy and add instructions that best match
                long bytesEncoded = EncodeCopyForBestMatch(hash, candidatePos, nextEncode, targetEnd);

                if (bytesEncoded > 0)
                {
                    nextEncode += bytesEncoded;
                    candidatePos = nextEncode;

                    if (candidatePos > startOfLastBlock)
                    {
                        break;
                    }

                    newData.Position = candidatePos;
                    //cannot use rolling hash since we skipped so many
                    hash = hasher.Hash(newData.ReadBytes(BlockHash.BlockSize));
                }
                else
                {
                    if (candidatePos + 1 > startOfLastBlock)
                    {
                        break;
                    }

                    //update hash requires the first byte of the last hash as well as the byte that is first byte pos + blockSize
                    //in order to properly calculate the rolling hash
                    newData.Position = candidatePos;
                    byte peek0 = newData.ReadByte();
                    newData.Position = candidatePos + BlockHash.BlockSize;
                    byte peek1 = newData.ReadByte();
                    hash = hasher.UpdateHash(hash, peek0, peek1);
                    candidatePos++;
                }
            }

            //Add the rest of the data that was not encoded
            if (nextEncode < newData.Length)
            {
                int len = (int)(newData.Length - nextEncode);
                newData.Position = nextEncode;
                windowEncoder.Add(newData.ReadBytes(len).ToArray());
            }

            //output the final window
            windowEncoder.Output(sout);
        }

        //currently does not support looking in target
        //only the dictionary
        private long EncodeCopyForBestMatch(ulong hash, long candidateStart, long unencodedStart, long unencodedSize)
        {
            BlockHash.Match bestMatch = new BlockHash.Match();

            dictionary.FindBestMatch(hash, candidateStart, unencodedStart, unencodedSize, newData, bestMatch);

            if (bestMatch.Size < MinBlockSize)
            {
                return 0;
            }

            if (bestMatch.TargetOffset > 0)
            {
                newData.Position = unencodedStart;
                windowEncoder.Add(newData.ReadBytes((int)bestMatch.TargetOffset).ToArray());
            }

            windowEncoder.Copy((int)bestMatch.SourceOffset, (int)bestMatch.Size);

            return bestMatch.Size + bestMatch.TargetOffset;
        }

        public void Dispose()
        {
            if (oldData != null)
            {
                oldData.Dispose();
            }
            if (newData != null)
            {
                newData.Dispose();
            }
            if (windowEncoder != null)
            {
                windowEncoder = null;
            }
        }
    }
}