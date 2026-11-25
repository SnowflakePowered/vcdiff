using System;
using System.IO;
using System.Threading.Tasks;
using VCDiff.Compressors;
using VCDiff.Includes;
using VCDiff.Shared;

namespace VCDiff.Decoders
{
    /// <summary>
    /// Applies VCDIFF patches using seekable dictionary and patch streams.
    /// Use <see cref="VcDecoderEx{TSourceBuffer,TDeltaBuffer}"/> with custom byte buffers.
    /// </summary>
    public class VcDecoder : VcDecoderEx<ByteStreamReader, ByteStreamReader>, IDisposable
    {
        private readonly bool _ownsSources;
        private bool _disposed;

        /// <summary>
        /// Creates a new VCDIFF decoder.
        /// </summary>
        /// <param name="source">The dictionary stream, or the base file.</param>
        /// <param name="delta">The stream containing the VCDIFF delta.</param>
        /// <param name="outputStream">The stream to write the output in.</param>
        /// <param name="maxTargetFileSize">Maximum decoded size of a single target window in bytes. Defaults to 64 MiB.</param>
        /// <param name="disableChecksums">Whether to disable checksums when applying the delta. This can be dangerous, but can be useful when the input file differs in ways that the delta does not reference.</param>
        public VcDecoder(Stream source, Stream delta, Stream outputStream, int maxTargetFileSize = WindowDecoderBase.DefaultMaxTargetFileSize, bool disableChecksums = false)
            : base(new ByteStreamReader(source), new ByteStreamReader(delta), outputStream, maxTargetFileSize, disableChecksums)
        {
            _ownsSources = true;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            base.Dispose();
            if (_ownsSources && !_disposed)
            {
                base.delta.Dispose();
                base.source.Dispose();
                _disposed = true;
            }

            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Reconstructs target data from a dictionary buffer and a VCDIFF patch buffer.
    /// </summary>
    /// <typeparam name="TSourceBuffer">Type of <see cref="IByteBuffer"/> used for the source buffer.</typeparam>
    /// <typeparam name="TDeltaBuffer">Type of <see cref="IByteBuffer"/> used for the delta buffer.</typeparam>
    public class VcDecoderEx<TSourceBuffer, TDeltaBuffer> : IDisposable where TSourceBuffer : IByteBuffer
                                                                        where TDeltaBuffer : IByteBuffer
    {
        /// <summary>Writable destination for reconstructed target bytes.</summary>
        protected Stream outputStream;
        /// <summary>Buffer providing the encoded patch bytes.</summary>
        protected TDeltaBuffer delta;
        /// <summary>Buffer providing the original dictionary bytes.</summary>
        protected TSourceBuffer source;
        /// <summary>Maximum permitted target-window size in bytes.</summary>
        protected int maxTargetFileSize;
        /// <summary>Whether to skip embedded target checksum verification.</summary>
        protected bool disableChecksums;
        private CustomCodeTableDecoder? customTable;
        /// <summary>VCDIFF file signature followed by the standard version and zero flags.</summary>
        protected static readonly byte[] MagicBytes = { 0xD6, 0xC3, 0xC4, 0x00, 0x00 };

        /// <summary>
        /// If the provided delta is in Shared-Dictionary Compression over HTTP (Sandwich) protocol.
        /// </summary>
        public bool IsSDCHFormat { get; private set; }

        private byte SecondaryCompressorId { get; set; }
        private ICompressor? secondaryCompressor;

        /// <summary>
        /// If the decoder has been initialized.
        /// </summary>
        protected bool IsInitialized { get; set; }

        /// <summary>
        /// Creates a new VCDIFF decoder.
        /// </summary>
        /// <param name="dict">The dictionary stream, or the base file.</param>
        /// <param name="delta">The stream containing the VCDIFF delta.</param>
        /// <param name="outputStream">The stream to write the output in.</param>
        /// <param name="maxTargetFileSize">Maximum decoded size of a single target window in bytes. Defaults to 64 MiB.</param>
        /// <param name="disableChecksums">Whether to disable checksums when applying the delta. This can be dangerous, but can be useful when the input file differs in ways that the delta does not reference.</param>
        public VcDecoderEx(TSourceBuffer dict, TDeltaBuffer delta, Stream outputStream, int maxTargetFileSize = WindowDecoderBase.DefaultMaxTargetFileSize, bool disableChecksums = false)
        {
            this.delta  = delta;
            this.source = dict;
            this.outputStream = outputStream;
            this.maxTargetFileSize = maxTargetFileSize;
            this.disableChecksums = disableChecksums;
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

            // secondary compression
            if ((hdr & (int)VCDiffCodeFlags.VCDDECOMPRESS) != 0)
            {
                if (!delta.CanRead) return VCDiffResult.EOD;

                SecondaryCompressorId = delta.ReadByte();
            }

            CustomCodeTableDecoder? decodedTable = null;
            if ((hdr & (int)VCDiffCodeFlags.VCDCODETABLE) != 0)
            {
                decodedTable = new CustomCodeTableDecoder();
                var result = decodedTable.Decode(ref delta);
                if (result != VCDiffResult.SUCCESS) return result;
            }

            if ((hdr & (int)VCDiffCodeFlags.VCDAPPHEADER) != 0)
            {
                if (!delta.CanRead) return VCDiffResult.EOD;
                
                int headerLength = VarIntBE.ParseInt32(delta);
                if (headerLength < 0) return (VCDiffResult)headerLength;
                if (headerLength > delta.Length - delta.Position) return VCDiffResult.EOD;
                delta.Position += headerLength;
            }


            // A secondary decompressor retains state across windows, including calls resumed after EOD.
            secondaryCompressor = CreateCompressor(SecondaryCompressorId);
            customTable = decodedTable;
            this.IsSDCHFormat = version == 'S';

            this.IsInitialized = true;

            return VCDiffResult.SUCCESS;
        }

        /// <summary>Decodes available windows and appends target bytes to the output stream.</summary>
        /// <param name="bytesWritten">Number of bytes written from complete, successfully decoded windows during this call.</param>
        /// <returns>The decode status: success, end of data, or a format error.</returns>
        /// <exception cref="NotSupportedException">The patch specifies an unsupported secondary compressor.</exception>
        /// <exception cref="InvalidOperationException">A target window exceeds the configured limit.</exception>
        public VCDiffResult Decode(out long bytesWritten)
        {
            bytesWritten = 0;
            var result = PrepareToDecode();
            if (result != VCDiffResult.SUCCESS)
                return result;

            while (delta.CanRead)
            {
                using var window = new WindowDecoder<TDeltaBuffer>(source.Length, delta, secondaryCompressor, maxTargetFileSize);
                result = ReadWindow(window, out bool interleaved);
                if (result != VCDiffResult.SUCCESS)
                    return result;

                using var body = new BodyDecoder<TDeltaBuffer, TSourceBuffer>(window, source, outputStream, customTable, disableChecksums);
                result = body.Decode(interleaved);
                if (result != VCDiffResult.SUCCESS)
                    return result;
                bytesWritten += body.TotalBytesDecoded;
            }

            return result;
        }

        /// <summary>Decodes available windows and writes the reconstructed bytes asynchronously.</summary>
        /// <returns>A task containing the decode status and the number of bytes written from complete, successfully decoded windows during this call.</returns>
        /// <remarks>Window parsing and dictionary reads are synchronous.</remarks>
        /// <exception cref="NotSupportedException">The patch specifies an unsupported secondary compressor.</exception>
        /// <exception cref="InvalidOperationException">A target window exceeds the configured limit.</exception>
        public async Task<(VCDiffResult result, long bytesWritten)> DecodeAsync()
        {
            long bytesWritten = 0;
            var result = PrepareToDecode();
            if (result != VCDiffResult.SUCCESS)
                return (result, bytesWritten);

            while (delta.CanRead)
            {
                using var window = new WindowDecoder<TDeltaBuffer>(source.Length, delta, secondaryCompressor, maxTargetFileSize);
                result = ReadWindow(window, out bool interleaved);
                if (result != VCDiffResult.SUCCESS)
                    return (result, bytesWritten);

                using var body = new BodyDecoder<TDeltaBuffer, TSourceBuffer>(window, source, outputStream, customTable, disableChecksums);
                result = await body.DecodeAsync(interleaved);
                if (result != VCDiffResult.SUCCESS)
                    return (result, bytesWritten);
                bytesWritten += body.TotalBytesDecoded;
            }

            return (result, bytesWritten);
        }

        private VCDiffResult PrepareToDecode()
        {
            if (IsInitialized)
                return delta.CanRead ? VCDiffResult.SUCCESS : VCDiffResult.EOD;

            long headerStart = delta.Position;
            var result = Initialize();
            if (result == VCDiffResult.EOD)
                delta.Position = headerStart;
            return result;
        }

        private VCDiffResult ReadWindow(WindowDecoder<TDeltaBuffer> window, out bool interleaved)
        {
            interleaved = false;
            long windowStart = delta.Position;
            if (!window.Decode(IsSDCHFormat, SecondaryCompressorId))
            {
                var result = (VCDiffResult)window.Result;
                if (result == VCDiffResult.EOD)
                    delta.Position = windowStart;
                return result;
            }

            if (IsSDCHFormat)
            {
                if (window.InstructionAndSizesLength == 0 && window.TargetWindowLength > 0)
                    return VCDiffResult.ERROR;
                interleaved = window.AddRunLength == 0 && window.AddressesForCopyLength == 0;
            }
            return VCDiffResult.SUCCESS;
        }

        private ICompressor? CreateCompressor(byte secondaryCompressorId)
        {
            return secondaryCompressorId switch
            {
                0 => null,
                // xdelta defines 1 to be "DJW static huffman"
                2 => new XzCompressor(),
                // xdelta defines 16 to be "FGK adaptive huffman" but says it's non-standard
                _ => throw new NotSupportedException($"Secondary compression id '{secondaryCompressorId}' is not supported.")
            };
        }

        /// <summary>
        /// Disposes the decoder
        /// </summary>
        public virtual void Dispose()
        {
            (secondaryCompressor as IDisposable)?.Dispose();
            secondaryCompressor = null;
        }
    }
}