using System;
using System.IO;
using VCDiff.Includes;
using VCDiff.Shared;

namespace VCDiff.Encoders
{
    /// <summary>
    /// A simple VCDIFF Encoder class.
    /// </summary>
    public class VcEncoder : IDisposable
    {
        private readonly IByteBuffer oldData;
        private readonly IByteBuffer newData;
        private readonly Stream outputStream;
        private readonly RollingHash hasher;
        private readonly int bufferSize;

        private static readonly byte[] MagicBytes = { 0xD6, 0xC3, 0xC4, 0x00, 0x00 };
        private static readonly byte[] MagicBytesExtended = { 0xD6, 0xC3, 0xC4, (byte)'S', 0x00 };
        private readonly int blockSize;
        private readonly int chunkSize;

        /// <summary>
        /// Creates a new VCDIFF Encoder.
        /// </summary>
        /// <param name="source">The dictionary (source file).</param>
        /// <param name="target">The target to create the diff from.</param>
        /// <param name="outputStream">The stream to write the diff into.</param>
        /// <param name="maxBufferSize">The maximum buffer size for window chunking in megabytes (MiB).</param>
        /// <param name="blockSize">
        /// The block size to use. Must be a power of two. No match smaller than this block size will be identified.
        /// Increasing blockSize by a factor of two will halve the amount of memory needed for the next block table, and will halve the setup time
        /// for a new BlockHash.  However, it also doubles the minimum match length that is guaranteed to be found. 
        /// </param>
        /// <param name="chunkSize">
        /// The minimum size of a string match that is worth putting into a COPY. This must be bigger than twice the block size.</param>
        /// <exception cref="ArgumentException">If an invalid blockSize or chunkSize is used..</exception>
        public VcEncoder(Stream source, Stream target, Stream outputStream, int maxBufferSize = 1, int blockSize = 16, int chunkSize = 0)
        {
            if (maxBufferSize <= 0) maxBufferSize = 1;
            this.blockSize = blockSize;
            this.chunkSize = chunkSize < 2 ? this.blockSize * 2 : chunkSize;
            this.oldData = new ByteBuffer(source);
            this.newData = new ByteStreamReader(target);
            this.outputStream = outputStream;
            this.hasher = new RollingHash(this.blockSize);
            this.bufferSize = maxBufferSize * 1024 * 1024;

            if (this.blockSize % 2 != 0 || this.chunkSize < 2 || this.chunkSize < 2 * this.blockSize)
            {
                throw new ArgumentException($"{this.blockSize} can not be less than 2 or twice the blocksize of the dictionary {this.blockSize}.");
            }

        }

        /// <summary>
        /// Calculate and write a diff for the file.
        /// </summary>
        /// <param name="interleaved">Whether to output in SDCH interleaved diff format.</param>
        /// <param name="checksumFormat">
        /// Whether to include Adler32 checksums for encoded data windows. If interleaved is true, <see cref="ChecksumFormat.Xdelta3"/>
        /// is not supported.
        /// </param>
        /// <returns>
        /// <see cref="VCDiffResult.SUCCESS"/> if successful, <see cref="VCDiffResult.ERROR"/> if the source or target are zero-length.</returns>
        /// <exception cref="ArgumentException">If interleaved is true, and <see cref="ChecksumFormat.Xdelta3"/> is chosen.</exception>
        public VCDiffResult Encode(bool interleaved = false, 
            ChecksumFormat checksumFormat = ChecksumFormat.None)
        {
            if (interleaved && checksumFormat == ChecksumFormat.Xdelta3)
            {
                throw new ArgumentException("Interleaved diffs can not have an xdelta3 checksum!");
            }

            if (newData.Length == 0 || oldData.Length == 0)
            {
                return VCDiffResult.ERROR;
            }

            VCDiffResult result = VCDiffResult.SUCCESS;

            oldData.Position = 0;
            newData.Position = 0;

            // file header
            // write magic bytes
            if (!interleaved && checksumFormat != ChecksumFormat.SDCH)
            {
                outputStream.Write(MagicBytes);
            }
            else
            {
                outputStream.Write(MagicBytesExtended);
            }

            //read in all the dictionary it is the only thing that needs to be
            BlockHash dictionary = new BlockHash(oldData, 0, hasher, blockSize);
            dictionary.AddAllBlocks();
            oldData.Position = 0;

            ChunkEncoder chunker = new ChunkEncoder(dictionary, oldData, hasher, checksumFormat, interleaved, chunkSize);

            while (newData.CanRead)
            {
                using ByteBuffer ntarget = new ByteBuffer(newData.ReadBytes(bufferSize));
                chunker.EncodeChunk(ntarget, outputStream);
            }

            return result;
        }

        /// <summary>
        /// Disposes the encoder.
        /// </summary>
        public void Dispose()
        {
            oldData.Dispose();
            newData.Dispose();
        }
    }
}