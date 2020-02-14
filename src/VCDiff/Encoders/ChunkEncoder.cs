using System;
using System.IO;
using VCDiff.Shared;

namespace VCDiff.Encoders
{
    internal class ChunkEncoder : IDisposable
    {
        public int MinBlockSize { get; }

        private BlockHash dictionary;
        private ByteBuffer oldData;
        private WindowEncoder? windowEncoder;
        private RollingHash hasher;
        private bool interleaved;
        private ChecksumFormat checksumFormat;

        /// <summary>
        /// Performs the actual encoding of a chunk of data into the VCDiff format
        /// </summary>
        /// <param name="dictionary">The dictionary hash table</param>
        /// <param name="oldData">The data for the dictionary hash table</param>
        /// <param name="hash">The rolling hash object</param>
        /// <param name="interleaved">Whether to interleave the data or not</param>
        /// <param name="checksumFormat">The format of the checksums for each window.</param>
        /// <param name="minBlockSize">The minimum block size to use. Defaults to 32, and must be a power of 2.
        ///     This value must also be smaller than the block size of the dictionary.</param>
        public ChunkEncoder(BlockHash dictionary, ByteBuffer oldData, 
            RollingHash hash, ChecksumFormat checksumFormat, bool interleaved = false, int minBlockSize = 32)
        {
            this.checksumFormat = checksumFormat;
            this.hasher = hash;
            this.oldData = oldData;
            this.dictionary = dictionary;
            this.MinBlockSize = minBlockSize;
            this.interleaved = interleaved;
        }

        /// <summary>
        /// Encodes the data using the settings from initialization
        /// </summary>
        /// <param name="newData">the target data</param>
        /// <param name="outputStream">the out stream</param>
        public void EncodeChunk(ByteBuffer newData, Stream outputStream)
        {
            newData.Position = 0;
            ReadOnlyMemory<byte> checksumBytes = newData.ReadBytes((int)newData.Length);

            uint checksum = this.checksumFormat switch
            {
                ChecksumFormat.SDCH => Checksum.ComputeGoogleAdler32(checksumBytes),
                ChecksumFormat.Xdelta3 => Checksum.ComputeXdelta3Adler32(checksumBytes),
                ChecksumFormat.None => 0,
                _ => 0
            };

            windowEncoder = new WindowEncoder(oldData.Length, checksum, this.checksumFormat, this.interleaved);

            oldData.Position = 0;
            newData.Position = 0;

            long nextEncode = newData.Position;
            long targetEnd = newData.Length;
            long startOfLastBlock = targetEnd - this.dictionary.blockSize;
            long candidatePos = nextEncode;

            ulong hash;
            //create the first hash
            unsafe
            {
                hash = hasher.Hash(newData.DangerousGetBytePointerAtCurrentPositionAndIncreaseOffsetAfter(0),
                    this.dictionary.blockSize);
            }

            while (true)
            {
                //if less than block size exit and then write as an ADD
                if (newData.Length - nextEncode < this.dictionary.blockSize)
                {
                    break;
                }

                //try and encode the copy and add instructions that best match
                long bytesEncoded = EncodeCopyForBestMatch(hash, candidatePos, nextEncode, targetEnd, newData);

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
                    unsafe
                    {
                        hash = hasher.Hash(newData.DangerousGetBytePointerAtCurrentPositionAndIncreaseOffsetAfter(this.dictionary.blockSize), this.dictionary.blockSize);
                    }
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
                    newData.Position = candidatePos + this.dictionary.blockSize;
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
                windowEncoder.Add(newData.ReadBytes(len).Span);
            }

            //output the final window
            windowEncoder.Output(outputStream);
        }

        //currently does not support looking in target
        //only the dictionary
        private long EncodeCopyForBestMatch(ulong hash, long candidateStart, long unencodedStart, long unencodedSize, ByteBuffer newData)
        {
            BlockHash.Match bestMatch = new BlockHash.Match();

            dictionary.FindBestMatch(hash, candidateStart, unencodedStart, unencodedSize, newData,
                ref bestMatch);

            if (bestMatch.Size < MinBlockSize)
            {
                return 0;
            }

            if (bestMatch.TargetOffset > 0)
            {
                newData.Position = unencodedStart;
                windowEncoder?.Add(newData.ReadBytes((int)bestMatch.TargetOffset).Span);
            }

            windowEncoder?.Copy((int)bestMatch.SourceOffset, (int)bestMatch.Size);

            return bestMatch.Size + bestMatch.TargetOffset;
        }

        public void Dispose()
        {
            oldData?.Dispose();
            windowEncoder?.Dispose();
        }
    }
}