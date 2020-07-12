﻿using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using VCDiff.Includes;
using VCDiff.Shared;

namespace VCDiff.Decoders
{
    /// <summary>
    /// A simple VCDIFF decoder class.
    /// </summary>
    public class VcDecoder : IDisposable
    {
        private readonly Stream outputStream;
        private readonly IByteBuffer delta;
        private readonly IByteBuffer source;
        private readonly int maxTargetFileSize;
        private CustomCodeTableDecoder? customTable;
        private static readonly byte[] MagicBytes = { 0xD6, 0xC3, 0xC4, 0x00, 0x00 };

        /// <summary>
        /// If the provided delta is in Shared-Dictionary Compression over HTTP (Sandwich) protocol.
        /// </summary>
        public bool IsSDCHFormat { get; private set; }

        /// <summary>
        /// If the decoder has been initialized.
        /// </summary>
        private bool IsInitialized { get; set; }

        /// <summary>
        /// Creates a new VCDIFF decoder.
        /// </summary>
        /// <param name="source">The dictionary stream, or the base file.</param>
        /// <param name="delta">The stream containing the VCDIFF delta.</param>
        /// <param name="outputStream">The stream to write the output in.</param>
        /// <param name="maxTargetFileSize">The maximum target file size (and target window size) in bytes</param>
        public VcDecoder(Stream source, Stream delta, Stream outputStream, int maxTargetFileSize = WindowDecoder.DefaultMaxTargetFileSize)
        {
            this.delta = new ByteStreamReader(delta);
            this.source = new ByteStreamReader(source);
            this.outputStream = outputStream;
            this.maxTargetFileSize = maxTargetFileSize;
            this.IsInitialized = false;
        }

        internal VcDecoder(IByteBuffer dict, IByteBuffer delta, Stream outputStream, int maxTargetFileSize = WindowDecoder.DefaultMaxTargetFileSize)
        {
            this.delta = delta;
            this.source = dict;
            this.outputStream = outputStream;
            this.maxTargetFileSize = maxTargetFileSize;
            this.IsInitialized = false;
        }

        /// <summary>
        /// Call this before calling decode
        /// This expects at least the header part of the delta file
        /// is available in the stream
        /// </summary>
        /// <returns></returns>
        private VCDiffResult Initialize()
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
                return VCDiffResult.ERROR;
            }

            if (C != MagicBytes[1])
            {
                return VCDiffResult.ERROR;
            }

            if (D != MagicBytes[2])
            {
                return VCDiffResult.ERROR;
            }

            if (version != 0x00 && version != 'S')
            {
                return VCDiffResult.ERROR;
            }

            //compression not supported
            if ((hdr & (int)VCDiffCodeFlags.VCDDECOMPRESS) != 0)
            {
                return VCDiffResult.ERROR;
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

            if ((hdr & (int)VCDiffCodeFlags.VCDAPPHEADER) != 0)
            {
                if (!delta.CanRead) return VCDiffResult.EOD;
                
                int headerLength = VarIntBE.ParseInt32(delta);
                // skip the app header
                delta.ReadBytes(headerLength);
            }


            this.IsSDCHFormat = version == 'S';

            this.IsInitialized = true;

            return VCDiffResult.SUCCESS;
        }

        /// <summary>
        /// Writes the patched file into the output stream.
        /// </summary>
        /// <param name="bytesWritten">Number of bytes written into the output stream.</param>
        /// <returns></returns>
        public VCDiffResult Decode(out long bytesWritten)
        {
            if (!this.IsInitialized)
            {
                var initializeResult = this.Initialize();
                if (initializeResult != VCDiffResult.SUCCESS || !this.IsInitialized)
                {
                    bytesWritten = 0;
                    return initializeResult;
                }
            }

            VCDiffResult result = VCDiffResult.SUCCESS;
            bytesWritten = 0;

            if (!delta.CanRead) return VCDiffResult.EOD;

            while (delta.CanRead)
            {
                //delta is streamed in order aka not random access
                WindowDecoder w = new WindowDecoder(source.Length, delta, maxTargetFileSize);

                if (w.Decode(this.IsSDCHFormat))
                {
                    using BodyDecoder body = new BodyDecoder(w, source, delta, outputStream);
                    if (this.IsSDCHFormat && w.AddRunLength == 0 && w.AddressesForCopyLength == 0 && w.InstructionAndSizesLength > 0)
                    {
                        //interleaved
                        //decodedinterleave actually has an internal loop for waiting and streaming the incoming rest of the interleaved window
                        result = body.DecodeInterleave();

                        if (result != VCDiffResult.SUCCESS && result != VCDiffResult.EOD)
                        {
                            return result;
                        }

                        bytesWritten += body.TotalBytesDecoded;
                    }
                    //technically add could be 0 if it is all copy instructions
                    //so do an or check on those two
                    else if (!this.IsSDCHFormat || (this.IsSDCHFormat && (w.AddRunLength > 0 || w.AddressesForCopyLength > 0) && w.InstructionAndSizesLength > 0))
                    {
                        //not interleaved
                        //expects the full window to be available
                        //in the stream

                        result = body.Decode();

                        if (result != VCDiffResult.SUCCESS)
                        {
                            return result;
                        }

                        bytesWritten += body.TotalBytesDecoded;
                    }
                    else
                    {
                        //invalid file
                        return VCDiffResult.ERROR;
                    }
                }
                else
                {
                    return (VCDiffResult)w.Result;
                }
            }

            return result;
        }

        /// <summary>
        /// Writes the patched file into the output stream asynchronously.
        /// This method is only asynchronous for the final step of writing the patched data into the output stream.
        /// For large outputs, this may be beneficial.
        /// </summary>
        /// <returns></returns>
        public async Task<(VCDiffResult result, long bytesWritten)> DecodeAsync()
        {
            long bytesWritten;
            if (!this.IsInitialized)
            {
                var initializeResult = this.Initialize();
                if (initializeResult != VCDiffResult.SUCCESS || !this.IsInitialized)
                {
                    bytesWritten = 0;
                    return (initializeResult, bytesWritten);
                }
            }

            VCDiffResult result = VCDiffResult.SUCCESS;
            bytesWritten = 0;

            if (!delta.CanRead) return (VCDiffResult.EOD, bytesWritten);

            while (delta.CanRead)
            {
                //delta is streamed in order aka not random access
                WindowDecoder w = new WindowDecoder(source.Length, delta, maxTargetFileSize);

                if (w.Decode(this.IsSDCHFormat))
                {
                    using BodyDecoder body = new BodyDecoder(w, source, delta, outputStream);
                    if (this.IsSDCHFormat && w.AddRunLength == 0 && w.AddressesForCopyLength == 0 && w.InstructionAndSizesLength > 0)
                    {
                        //interleaved
                        //decodedinterleave actually has an internal loop for waiting and streaming the incoming rest of the interleaved window
                        result = await body.DecodeInterleaveAsync();

                        if (result != VCDiffResult.SUCCESS && result != VCDiffResult.EOD)
                        {
                            return (result, bytesWritten);
                        }

                        bytesWritten += body.TotalBytesDecoded;
                    }
                    //technically add could be 0 if it is all copy instructions
                    //so do an or check on those two
                    else if (!this.IsSDCHFormat || (this.IsSDCHFormat && (w.AddRunLength > 0 || w.AddressesForCopyLength > 0) && w.InstructionAndSizesLength > 0))
                    {
                        //not interleaved
                        //expects the full window to be available
                        //in the stream

                        result = await body.DecodeAsync();

                        if (result != VCDiffResult.SUCCESS)
                        {
                            return (result, bytesWritten);
                        }

                        bytesWritten += body.TotalBytesDecoded;
                    }
                    else
                    {
                        //invalid file
                        return (VCDiffResult.ERROR, bytesWritten);
                    }
                }
                else
                {
                    return ((VCDiffResult)w.Result, bytesWritten);
                }
            }

            return (result, bytesWritten);
        }
        /// <summary>
        /// Disposes the decoder
        /// </summary>
        public void Dispose()
        {
            (delta as ByteBuffer)?.Dispose();
            (source as ByteBuffer)?.Dispose();
        }
    }
}