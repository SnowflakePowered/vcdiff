using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VCDiff.Includes;
using VCDiff.Shared;

namespace VCDiff.Decoders
{
    internal class BodyDecoder : IDisposable
    {
        private WindowDecoder window;
        private Stream outputStream;
        private IByteBuffer source;
        private IByteBuffer delta;
        private AddressCache addressCache;
        private MemoryStream targetData;
        private CustomCodeTableDecoder? customTable;

        //the total bytes decoded
        public long TotalBytesDecoded { get; private set; }

        /// <summary>
        /// The main decoder loop for the data
        /// </summary>
        /// <param name="w">the window decoder</param>
        /// <param name="source">The source dictionary data</param>
        /// <param name="delta">The delta</param>
        /// <param name="decodedTarget">the out stream</param>
        /// <param name="customTable">custom table if any. Default is null.</param>
        public BodyDecoder(WindowDecoder w, IByteBuffer source, IByteBuffer delta, Stream decodedTarget, CustomCodeTableDecoder? customTable = null)
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
            this.delta = delta;
            this.targetData = new MemoryStream();
        }

        private VCDiffResult DecodeInterleaveCore()
        {
            VCDiffResult result = VCDiffResult.SUCCESS;
            //since interleave expected then the last point that was most likely decoded was the lengths section
            //so following is all data for the add run copy etc
            long interleaveLength = window.InstructionAndSizesLength;
            using var previous = new MemoryStream();
            int lastDecodedSize = 0;
            VCDiffInstructionType lastDecodedInstruction = VCDiffInstructionType.NOOP;

            while (interleaveLength > 0)
            {
                if (!delta.CanRead) continue;
                //read in
                var didBreakBeforeComplete = false;

                //try to read in all interleaved bytes
                //if not then it will buffer for next time
                previous.Write(delta.ReadBytesAsSpan((int)interleaveLength));
                using ByteBuffer incoming = new ByteBuffer(previous.ToArray());
                previous.SetLength(0);
                long initialLength = incoming.Length;

                InstructionDecoder instrDecoder = new InstructionDecoder(incoming, customTable);

                while (incoming.CanRead && TotalBytesDecoded < window.TargetWindowLength)
                {
                    int decodedSize = 0;
                    byte mode = 0;
                    VCDiffInstructionType instruction = VCDiffInstructionType.NOOP;

                    if (lastDecodedSize > 0 && lastDecodedInstruction != VCDiffInstructionType.NOOP)
                    {
                        decodedSize = lastDecodedSize;
                        instruction = lastDecodedInstruction;
                    }
                    else
                    {
                        instruction = instrDecoder.Next(out decodedSize, out mode);

                        switch (instruction)
                        {
                            case VCDiffInstructionType.EOD:
                                didBreakBeforeComplete = true;
                                break;

                            case VCDiffInstructionType.ERROR:
                                targetData.SetLength(0);
                                return VCDiffResult.ERROR;
                        }
                    }

                    //if instruction is EOD then decodedSize will be 0 as well
                    //the last part of the buffer containing the instruction will be
                    //buffered for the next loop
                    lastDecodedInstruction = instruction;
                    lastDecodedSize = decodedSize;

                    if (didBreakBeforeComplete)
                    {
                        //we don't have all the data so store this pointer into a temporary list to resolve next loop
                        didBreakBeforeComplete = true;
                        interleaveLength -= incoming.Position;

                        if (initialLength - incoming.Position > 0)
                        {
                            previous.Write(incoming.ReadBytesAsSpan((int)(initialLength - incoming.Position)));
                        }

                        break;
                    }

                    switch (instruction)
                    {
                        case VCDiffInstructionType.ADD:
                            result = DecodeAdd(decodedSize, incoming);
                            break;

                        case VCDiffInstructionType.RUN:
                            result = DecodeRun(decodedSize, incoming);
                            break;

                        case VCDiffInstructionType.COPY:
                            result = DecodeCopy(decodedSize, mode, incoming);
                            break;

                        default:
                            targetData.SetLength(0);
                            return VCDiffResult.ERROR;
                    }

                    if (result == VCDiffResult.EOD)
                    {
                        //we don't have all the data so store this pointer into a temporary list to resolve next loop
                        didBreakBeforeComplete = true;
                        interleaveLength -= incoming.Position;

                        if (initialLength - incoming.Position > 0)
                        {
                            previous.Write(incoming.ReadBytesAsSpan((int)(initialLength - incoming.Position)));
                        }

                        break;
                    }

                    //reset these as we have successfully used them
                    lastDecodedInstruction = VCDiffInstructionType.NOOP;
                    lastDecodedSize = 0;
                }

                if (!didBreakBeforeComplete)
                {
                    interleaveLength -= initialLength;
                }
            }

            if (window.ChecksumFormat == ChecksumFormat.SDCH)
            {
                uint adler = Checksum.ComputeGoogleAdler32(targetData.GetBuffer().AsMemory(0, (int)targetData.Length).Span);

                if (adler != window.Checksum)
                {
                    result = VCDiffResult.ERROR;
                }
            }

            return result;
        }
        
        /// <summary>
        /// Decode if as expecting interleave
        /// </summary>
        /// <returns></returns>
        public VCDiffResult DecodeInterleave()
        {
            var result = DecodeInterleaveCore();
            targetData.Seek(0, SeekOrigin.Begin);
            targetData.CopyTo(outputStream);
            targetData.SetLength(0);
            return result;
        }

        /// <summary>
        /// Decode if as expecting interleave
        /// </summary>
        /// <returns></returns>
        public async Task<VCDiffResult> DecodeInterleaveAsync(CancellationToken token = default)
        {
            var result = DecodeInterleaveCore();
            targetData.Seek(0, SeekOrigin.Begin);
            await targetData.CopyToAsync(outputStream, token);
            targetData.SetLength(0);
            return result;
        }

        private VCDiffResult DecodeCore()
        {
            using ByteBuffer instructionBuffer = new ByteBuffer(window.InstructionsAndSizesData);
            using ByteBuffer addressBuffer = new ByteBuffer(window.AddressesForCopyData);
            using ByteBuffer addRunBuffer = new ByteBuffer(window.AddRunData);

            InstructionDecoder instrDecoder = new InstructionDecoder(instructionBuffer, customTable);

            VCDiffResult result = VCDiffResult.SUCCESS;

            while (this.TotalBytesDecoded < window.TargetWindowLength)
            {
                VCDiffInstructionType instruction = instrDecoder.Next(out int decodedSize, out byte mode);

                switch (instruction)
                {
                    case VCDiffInstructionType.EOD:
                        targetData.SetLength(0);
                        return VCDiffResult.EOD;

                    case VCDiffInstructionType.ERROR:
                        targetData.SetLength(0);
                        return VCDiffResult.ERROR;
                }

                switch (instruction)
                {
                    case VCDiffInstructionType.ADD:
                        result = DecodeAdd(decodedSize, addRunBuffer);
                        break;

                    case VCDiffInstructionType.RUN:
                        result = DecodeRun(decodedSize, addRunBuffer);
                        break;

                    case VCDiffInstructionType.COPY:
                        result = DecodeCopy(decodedSize, mode, addressBuffer);
                        break;

                    default:
                        targetData.SetLength(0);
                        return VCDiffResult.ERROR;
                }
            }

            if (window.ChecksumFormat == ChecksumFormat.SDCH)
            {
                uint adler = Checksum.ComputeGoogleAdler32(targetData.GetBuffer().AsMemory(0, (int)targetData.Length).Span);

                if (adler != window.Checksum)
                {
                    result = VCDiffResult.ERROR;
                }
            }
            else if (window.ChecksumFormat == ChecksumFormat.Xdelta3)
            {
                uint adler = Checksum.ComputeXdelta3Adler32(targetData.GetBuffer().AsMemory(0, (int)targetData.Length).Span);

                if (adler != window.Checksum)
                {
                    result = VCDiffResult.ERROR;
                }
            }

            return result;
        }
        
        /// <summary>
        /// Decode normally
        /// </summary>
        /// <returns></returns>
        public VCDiffResult Decode()
        {
            var result = this.DecodeCore();
            targetData.Seek(0, SeekOrigin.Begin);
            targetData.CopyTo(outputStream);
            targetData.SetLength(0);
            return result;
        }

        /// <summary>
        /// Decode normally
        /// </summary>
        /// <returns></returns>
        public async Task<VCDiffResult> DecodeAsync(CancellationToken token = default)
        {
            var result = this.DecodeCore();
            targetData.Seek(0, SeekOrigin.Begin);
            await targetData.CopyToAsync(outputStream, token);
            targetData.SetLength(0);
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

            // Copy all data from source segment
            if (decodedAddress + size <= window.SourceSegmentLength)
            {
                source.Position = decodedAddress + window.SourceSegmentOffset;
                targetData.Write(source.ReadBytesAsSpan(size));
                this.TotalBytesDecoded += size;
                return VCDiffResult.SUCCESS;
            }

            // Copy some data from target window...
            if (decodedAddress < window.SourceSegmentLength)
            {
                // ... plus some data from source segment
                long partialCopySize = window.SourceSegmentLength - decodedAddress;
                source.Position = decodedAddress + +window.SourceSegmentOffset;
                targetData.Write(source.ReadBytesAsSpan((int)partialCopySize));
                this.TotalBytesDecoded += partialCopySize;
                decodedAddress += partialCopySize;
                size -= (int)partialCopySize;
            }

            decodedAddress -= window.SourceSegmentLength;
            bool overlap = decodedAddress + size >= this.TotalBytesDecoded;
            if (overlap)
            {
                int availableData = (int)(this.TotalBytesDecoded - decodedAddress);
                for (int i = 0; i < size; i += availableData)
                {
                    int toCopy = (size - i < availableData) ? size - i : availableData;
                    var tbytesBuf = targetData.GetBuffer().AsSpan((int)decodedAddress + i, toCopy);
                    //outputStream.Write(tbytesBuf);
                    targetData.Write(tbytesBuf);
                    this.TotalBytesDecoded += toCopy;
                }
            }
            else
            {
                var fbytes = targetData.GetBuffer().AsSpan((int)decodedAddress, size);
                //outputStream.Write(fbytes);
                targetData.Write(fbytes);
                this.TotalBytesDecoded += size;
            }
            return VCDiffResult.SUCCESS;

        }

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

            byte b = addRun.ReadByte();

            for (int i = 0; i < size; ++i)
            {
                //outputStream.Write(b);
                targetData.WriteByte(b);
            }

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
            
            targetData.Write(addRun.ReadBytesAsSpan(size));
            TotalBytesDecoded += size;
            return VCDiffResult.SUCCESS;
        }

        public void Dispose()
        {
            targetData.Dispose();
        }
    }
}
