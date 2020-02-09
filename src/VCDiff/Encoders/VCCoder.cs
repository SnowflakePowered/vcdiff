using System;
using System.IO;
using VCDiff.Includes;
using VCDiff.Shared;

namespace VCDiff.Encoders
{
    public class VCCoder
    {
        private IByteBuffer oldData;
        private IByteBuffer newData;
        private ByteStreamWriter outputStreamWriter;
        private RollingHash hasher;
        private int bufferSize;

        private static byte[] MagicBytes = { 0xD6, 0xC3, 0xC4, 0x00, 0x00 };
        private static byte[] MagicBytesExtended = { 0xD6, 0xC3, 0xC4, (byte)'S', 0x00 };

        /// <summary>
        /// The easy public structure for encoding into a vcdiff format
        /// Simply instantiate it with the proper streams and use the Encode() function.
        /// Does not check if data is equal already. You will need to do that.
        /// Returns VCDiffResult: should always return success, unless either the dict or the target streams have 0 bytes
        /// See the VCDecoder for decoding vcdiff format
        /// </summary>
        /// <param name="source">The dictionary (previous data)</param>
        /// <param name="target">The new data</param>
        /// <param name="outputStream">The output stream</param>
        /// <param name="maxBufferSize">The maximum buffer size for window chunking. It is in Megabytes. 2 would mean 2 megabytes etc. Default is 1.</param>
        public VCCoder(Stream source, Stream target, Stream outputStream, int maxBufferSize = 1)
        {
            if (maxBufferSize <= 0) maxBufferSize = 1;

            oldData = new ByteBuffer(source);
            newData = new ByteStreamReader(target);
            outputStreamWriter = new ByteStreamWriter(outputStream);
            hasher = new RollingHash(BlockHash.BlockSize);

            bufferSize = maxBufferSize * 1024 * 1024;
        }

        /// <summary>
        /// Encodes the file
        /// </summary>
        /// <param name="interleaved">Set this to true to enable SDHC interleaved vcdiff google format</param>
        /// <param name="checksum">Set this to true to add checksum for encoded data windows</param>
        /// <returns></returns>
        public VCDiffResult Encode(bool interleaved = false, bool checksum = false)
        {
            if (newData.Length == 0 || oldData.Length == 0)
            {
                return VCDiffResult.ERRROR;
            }

            VCDiffResult result = VCDiffResult.SUCCESS;

            oldData.Position = 0;
            newData.Position = 0;

            //file header
            //write magic bytes
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
    }
}