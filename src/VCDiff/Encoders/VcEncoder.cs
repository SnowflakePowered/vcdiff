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
        private readonly ByteStreamWriter outputStreamWriter;
        private readonly RollingHash hasher;
        private readonly int bufferSize;

        private static readonly byte[] MagicBytes = { 0xD6, 0xC3, 0xC4, 0x00, 0x00 };
        private static readonly byte[] MagicBytesExtended = { 0xD6, 0xC3, 0xC4, (byte)'S', 0x00 };

        /// <summary>
        /// Creates a new VCDIFF Encoder.
        /// </summary>
        /// <param name="source">The dictionary (source file).</param>
        /// <param name="target">The target to create the diff from.</param>
        /// <param name="outputStream">The stream to write the diff into.</param>
        /// <param name="maxBufferSize">The maximum buffer size for window chunking in megabytes (MiB).</param>
        public VcEncoder(Stream source, Stream target, Stream outputStream, int maxBufferSize = 1)
        {
            if (maxBufferSize <= 0) maxBufferSize = 1;

            oldData = new ByteBuffer(source);
            newData = new ByteStreamReader(target);
            outputStreamWriter = new ByteStreamWriter(outputStream);
            hasher = new RollingHash(BlockHash.BlockSize);

            bufferSize = maxBufferSize * 1024 * 1024;
        }

        /// <summary>
        /// Calculates the diff for the file.
        /// </summary>
        /// <param name="interleaved">Whether to output in SDCH interleaved diff format.</param>
        /// <param name="checksum">Whether to include Adler32 checksums for encoded data windows</param>
        /// <returns><see cref="VCDiffResult.SUCCESS"/> if successful, <see cref="VCDiffResult.ERROR"/> if the source or target are zero-length.</returns>
        public VCDiffResult Encode(bool interleaved = false, bool checksum = false)
        {
            if (newData.Length == 0 || oldData.Length == 0)
            {
                return VCDiffResult.ERROR;
            }

            VCDiffResult result = VCDiffResult.SUCCESS;

            oldData.Position = 0;
            newData.Position = 0;

            // file header
            // write magic bytes
            if (!interleaved && !checksum)
            {
                outputStreamWriter.Write(MagicBytes);
            }
            else
            {
                outputStreamWriter.Write(MagicBytesExtended);
            }

            //read in all the dictionary it is the only thing that needs to be
            BlockHash dictionary = new BlockHash(oldData, 0, hasher);
            dictionary.AddAllBlocks();
            oldData.Position = 0;

            ChunkEncoder chunker = new ChunkEncoder(dictionary, oldData, hasher, interleaved, checksum);

            while (newData.CanRead)
            {
                using ByteBuffer ntarget = new ByteBuffer(newData.ReadBytes(bufferSize));
                chunker.EncodeChunk(ntarget, outputStreamWriter);
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
            outputStreamWriter.Dispose();
        }
    }
}