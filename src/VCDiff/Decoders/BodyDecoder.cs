using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VCDiff.Includes;
using VCDiff.Shared;

namespace VCDiff.Decoders
{
    internal class BodyDecoder<TWindowDecoderByteBuffer, TSourceBuffer> : IDisposable
        where TWindowDecoderByteBuffer : IByteBuffer
        where TSourceBuffer : IByteBuffer
    {
        private WindowDecoder<TWindowDecoderByteBuffer> window;
        private Stream outputStream;
        private TSourceBuffer source;
        private AddressCache addressCache;
        private readonly byte[] targetData;
        private CustomCodeTableDecoder? customTable;
        private readonly bool disableChecksums;

        //the total bytes decoded
        public long TotalBytesDecoded { get; private set; }

        /// <summary>
        /// The main decoder loop for the data
        /// </summary>
        /// <param name="w">the window decoder</param>
        /// <param name="source">The source dictionary data</param>
        /// <param name="decodedTarget">the out stream</param>
        /// <param name="customTable">custom table if any. Default is null.</param>
        /// <param name="disableChecksums">Skip verification of the decoded window checksum when true.</param>
        public BodyDecoder(WindowDecoder<TWindowDecoderByteBuffer> w, TSourceBuffer source, Stream decodedTarget, CustomCodeTableDecoder? customTable = null, bool disableChecksums = false)
        {
            if (customTable != null)
            {
                this.customTable = customTable;
                addressCache = new AddressCache(customTable.NearSize, customTable.SameSize);
            }
            else
            {
                addressCache = new AddressCache();
            }
            window = w;
            this.outputStream = decodedTarget;
            this.source = source;
            targetData = ArrayPool<byte>.Shared.Rent(checked((int)w.TargetWindowLength));
            this.disableChecksums = disableChecksums;
        }

        private VCDiffResult DecodeWindow(bool interleaved)
        {
            using var instructions = new ByteBuffer(window.InstructionsAndSizesData.AsSpanOrDefault());
            VCDiffResult result;
            if (interleaved)
            {
                // Opcodes, literals and addresses all advance the same cursor.
                result = DecodeInstructions(instructions, instructions, instructions);
            }
            else
            {
                using var literals = new ByteBuffer(window.AddRunData.AsSpanOrDefault());
                using var addresses = new ByteBuffer(window.AddressesForCopyData.AsSpanOrDefault());
                result = DecodeInstructions(instructions, literals, addresses);
            }

            if (result != VCDiffResult.SUCCESS || TotalBytesDecoded != window.TargetWindowLength)
                return VCDiffResult.ERROR;

            if (!disableChecksums && window.ChecksumFormat != ChecksumFormat.None)
            {
                var bytes = targetData.AsSpan(0, (int)TotalBytesDecoded);
                uint checksum = window.ChecksumFormat == ChecksumFormat.SDCH
                    ? Checksum.ComputeGoogleAdler32(bytes)
                    : Checksum.ComputeXdelta3Adler32(bytes);
                if (checksum != window.Checksum)
                    return VCDiffResult.ERROR;
            }

            return VCDiffResult.SUCCESS;
        }

        private VCDiffResult DecodeInstructions(ByteBuffer instructions, ByteBuffer literals, ByteBuffer addresses)
        {
            var decoder = new InstructionDecoder(instructions, customTable);
            while (TotalBytesDecoded < window.TargetWindowLength)
            {
                var instruction = decoder.Next(out int size, out byte mode);
                if (size < 0 || size > window.TargetWindowLength - TotalBytesDecoded)
                    return VCDiffResult.ERROR;

                var result = instruction switch
                {
                    VCDiffInstructionType.ADD => DecodeAdd(size, literals),
                    VCDiffInstructionType.RUN => DecodeRun(size, literals),
                    VCDiffInstructionType.COPY => DecodeCopy(size, mode, addresses),
                    _ => VCDiffResult.ERROR
                };
                // The complete encoded window is already present. A missing operand is a format error.
                if (result != VCDiffResult.SUCCESS)
                    return VCDiffResult.ERROR;
            }
            return VCDiffResult.SUCCESS;
        }

        public VCDiffResult Decode(bool interleaved)
        {
            var result = DecodeWindow(interleaved);
            if (result == VCDiffResult.SUCCESS)
                outputStream.Write(targetData, 0, (int)TotalBytesDecoded);
            return result;
        }

        public async Task<VCDiffResult> DecodeAsync(bool interleaved, CancellationToken token = default)
        {
            var result = DecodeWindow(interleaved);
            if (result == VCDiffResult.SUCCESS)
                await outputStream.WriteAsync(targetData, 0, (int)TotalBytesDecoded, token);
            return result;
        }

        private VCDiffResult DecodeCopy(int size, byte mode, ByteBuffer addresses)
        {
            long hereAddress = window.SourceSegmentLength + this.TotalBytesDecoded;
            long decodedAddress = addressCache.DecodeAddress(hereAddress, mode, addresses);
            switch ((VCDiffResult)decodedAddress)
            {
                case VCDiffResult.ERROR:
                    return VCDiffResult.ERROR;

                case VCDiffResult.EOD:
                    return VCDiffResult.EOD;

                default:
                    if (decodedAddress < 0 || decodedAddress > hereAddress)
                    {
                        return VCDiffResult.ERROR;
                    }
                    break;
            }

            if (decodedAddress < window.SourceSegmentLength)
            {
                int fromSource = (int)Math.Min(size, window.SourceSegmentLength - decodedAddress);
                source.Position = window.SourceSegmentOffset + decodedAddress;
                if (!BufferReads.TryReadExactly(ref source, NextBytes(fromSource)))
                    return VCDiffResult.ERROR;

                TotalBytesDecoded += fromSource;
                size -= fromSource;
                if (size == 0)
                    return VCDiffResult.SUCCESS;

                decodedAddress += fromSource;
            }

            decodedAddress -= window.SourceSegmentLength;
            if (decodedAddress >= TotalBytesDecoded)
                return VCDiffResult.ERROR;
            while (size > 0)
            {
                // Previously copied bytes can serve as source for an overlapping COPY.
                int count = (int)Math.Min(size, TotalBytesDecoded - decodedAddress);
                targetData.AsSpan((int)decodedAddress, count).CopyTo(NextBytes(count));
                TotalBytesDecoded += count;
                size -= count;
            }
            return VCDiffResult.SUCCESS;
        }

        private Span<byte> NextBytes(int size) => targetData.AsSpan((int)TotalBytesDecoded, size);

        private VCDiffResult DecodeRun(int size, ByteBuffer addRun)
        {
            if (addRun.Position + 1 > addRun.Length)
            {
                return VCDiffResult.EOD;
            }

            if (!addRun.CanRead)
            {
                return VCDiffResult.EOD;
            }

            NextBytes(size).Fill(addRun.ReadByte());
            TotalBytesDecoded += size;

            return VCDiffResult.SUCCESS;
        }

        private VCDiffResult DecodeAdd(int size, ByteBuffer addRun)
        {
            if (addRun.Position + size > addRun.Length)
            {
                return VCDiffResult.EOD;
            }

            if (!addRun.CanRead)
            {
                return VCDiffResult.EOD;
            }
            
            addRun.ReadBytesAsSpan(size).CopyTo(NextBytes(size));
            TotalBytesDecoded += size;
            return VCDiffResult.SUCCESS;
        }

        public void Dispose()
        {
            ArrayPool<byte>.Shared.Return(targetData);
        }
    }
}
