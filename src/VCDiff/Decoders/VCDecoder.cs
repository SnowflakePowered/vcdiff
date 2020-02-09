using System.IO;
using VCDiff.Includes;
using VCDiff.Shared;

namespace VCDiff.Decoders
{
    public class VCDecoder
    {
        private readonly ByteStreamWriter outputStream;
        private readonly IByteBuffer delta;
        private readonly IByteBuffer source;
        private CustomCodeTableDecoder customTable;
        private static readonly byte[] MagicBytes = { 0xD6, 0xC3, 0xC4, 0x00, 0x00 };

        public bool IsSDHCFormat { get; private set; }

        public bool IsInitialized { get; private set; }

        /// <summary>
        /// Dict is the dictionary file
        /// Delta is the diff file
        /// Sout is the stream for output
        /// </summary>
        /// <param name="source">Dictionary</param>
        /// <param name="delta">Target file / Diff / Delta file</param>
        /// <param name="outputStream">Output Stream</param>
        public VCDecoder(Stream source, Stream delta, Stream outputStream)
        {
            this.delta = new ByteBuffer(delta);
            this.source = new ByteBuffer(source);
            this.outputStream = new ByteStreamWriter(outputStream);
            IsInitialized = false;
        }

        internal VCDecoder(IByteBuffer dict, IByteBuffer delta, Stream sout)
        {
            this.delta = delta;
            source = dict;
            outputStream = new ByteStreamWriter(sout);
            IsInitialized = false;
        }

        /// <summary>
        /// Call this before calling decode
        /// This expects at least the header part of the delta file
        /// is available in the stream
        /// </summary>
        /// <returns></returns>
        public VCDiffResult Initialize()
        {
            if (!delta.CanRead) return VCDiffResult.EOD;

            byte V = delta.ReadByte();

            if (!delta.CanRead) return VCDiffResult.EOD;

            byte C = delta.ReadByte();

            if (!delta.CanRead) return VCDiffResult.EOD;

            byte D = delta.ReadByte();

            if (!delta.CanRead) return VCDiffResult.EOD;

            byte version = delta.ReadByte();

            if (!delta.CanRead) return VCDiffResult.EOD;

            byte hdr = delta.ReadByte();

            if (V != MagicBytes[0])
            {
                return VCDiffResult.ERRROR;
            }

            if (C != MagicBytes[1])
            {
                return VCDiffResult.ERRROR;
            }

            if (D != MagicBytes[2])
            {
                return VCDiffResult.ERRROR;
            }

            if (version != 0x00 && version != 'S')
            {
                return VCDiffResult.ERRROR;
            }

            //compression not supported
            if ((hdr & (int)VCDiffCodeFlags.VCDDECOMPRESS) != 0)
            {
                return VCDiffResult.ERRROR;
            }

            //custom code table!
            if ((hdr & (int)VCDiffCodeFlags.VCDCODETABLE) != 0)
            {
                if (!delta.CanRead) return VCDiffResult.EOD;

                //try decoding the custom code table
                //since we don't support the compress the next line should be the length of the code table
                customTable = new CustomCodeTableDecoder();
                VCDiffResult result = customTable.Decode(delta);

                if (result != VCDiffResult.SUCCESS)
                {
                    return result;
                }
            }

            IsSDHCFormat = version == 'S';

            IsInitialized = true;

            return VCDiffResult.SUCCESS;
        }

        /// <summary>
        /// Use this after calling Start
        /// Each time the decode is called it is expected
        /// that at least 1 Window header is available in the stream
        /// </summary>
        /// <param name="bytesWritten">bytes decoded for all available windows</param>
        /// <returns></returns>
        public VCDiffResult Decode(out long bytesWritten)
        {
            if (!IsInitialized)
            {
                bytesWritten = 0;
                return VCDiffResult.ERRROR;
            }

            VCDiffResult result = VCDiffResult.SUCCESS;
            bytesWritten = 0;

            if (!delta.CanRead) return VCDiffResult.EOD;

            while (delta.CanRead)
            {
                //delta is streamed in order aka not random access
                WindowDecoder w = new WindowDecoder(source.Length, delta);

                if (w.Decode(IsSDHCFormat))
                {
                    using (BodyDecoder body = new BodyDecoder(w, source, delta, outputStream))
                    {
                        if (IsSDHCFormat && w.AddRunLength == 0 && w.AddressesForCopyLength == 0 && w.InstructionAndSizesLength > 0)
                        {
                            //interleaved
                            //decodedinterleave actually has an internal loop for waiting and streaming the incoming rest of the interleaved window
                            result = body.DecodeInterleave();

                            if (result != VCDiffResult.SUCCESS && result != VCDiffResult.EOD)
                            {
                                return result;
                            }

                            bytesWritten += body.Decoded;
                        }
                        //technically add could be 0 if it is all copy instructions
                        //so do an or check on those two
                        else if (IsSDHCFormat && (w.AddRunLength > 0 || w.AddressesForCopyLength > 0) && w.InstructionAndSizesLength > 0)
                        {
                            //not interleaved
                            //expects the full window to be available
                            //in the stream

                            result = body.Decode();

                            if (result != VCDiffResult.SUCCESS)
                            {
                                return result;
                            }

                            bytesWritten += body.Decoded;
                        }
                        else if (!IsSDHCFormat)
                        {
                            //not interleaved
                            //expects the full window to be available
                            //in the stream
                            result = body.Decode();

                            if (result != VCDiffResult.SUCCESS)
                            {
                                return result;
                            }

                            bytesWritten += body.Decoded;
                        }
                        else
                        {
                            //invalid file
                            return VCDiffResult.ERRROR;
                        }
                    }
                }
                else
                {
                    return (VCDiffResult)w.Result;
                }
            }

            return result;
        }
    }
}