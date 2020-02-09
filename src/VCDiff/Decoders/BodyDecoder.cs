using System;
using System.IO;
using VCDiff.Includes;
using VCDiff.Shared;

namespace VCDiff.Decoders
{
    internal class BodyDecoder : IDisposable
    {
        private WindowDecoder window;
        private ByteStreamWriter sout;
        private IByteBuffer dict;
        private IByteBuffer target;
        private AddressCache addressCache;
        private MemoryStream targetData;
        private CustomCodeTableDecoder? customTable;

        //the total bytes decoded
        public long Decoded { get; private set; }

        /// <summary>
        /// The main decoder loop for the data
        /// </summary>
        /// <param name="w">the window decoder</param>
        /// <param name="dictionary">the dictionary data</param>
        /// <param name="target">the target data</param>
        /// <param name="sout">the out stream</param>
        /// <param name="customTable">custom table if any. Default is null.</param>
        public BodyDecoder(WindowDecoder w, IByteBuffer dictionary, IByteBuffer target, ByteStreamWriter sout, CustomCodeTableDecoder? customTable = null)
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
            this.sout = sout;
            dict = dictionary;
            this.target = target;
            targetData = new MemoryStream();
        }

        /// <summary>
        /// Decode if as expecting interleave
        /// </summary>
        /// <returns></returns>
        public VCDiffResult DecodeInterleave()
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
                if (!target.CanRead) continue;
                //read in
                var didBreakBeforeComplete = false;

                //try to read in all interleaved bytes
                //if not then it will buffer for next time
                previous.Write(target.ReadBytes((int)interleaveLength).Span);
                ByteBuffer incoming = new ByteBuffer(previous.ToArray());
                previous.SetLength(0);
                long initialLength = incoming.Length;

                InstructionDecoder instrDecoder = new InstructionDecoder(incoming, customTable);

                while (incoming.CanRead && Decoded < window.DecodedDeltaLength)
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
                            previous.Write(incoming.ReadBytes((int)(initialLength - incoming.Position)).Span);
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
                            previous.Write(incoming.ReadBytes((int)(initialLength - incoming.Position)).Span);
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

            if (window.HasChecksum)
            {
                uint adler = Checksum.ComputeAdler32(targetData.ToArray());

                if (adler != window.Checksum)
                {
                    result = VCDiffResult.ERROR;
                }
            }

            targetData.SetLength(0);
            return result;
        }

        /// <summary>
        /// Decode normally
        /// </summary>
        /// <returns></returns>
        public VCDiffResult Decode()
        {
            ByteBuffer instructionBuffer = new ByteBuffer(window.InstructionsAndSizesData);
            ByteBuffer addressBuffer = new ByteBuffer(window.AddressesForCopyData);
            ByteBuffer addRunBuffer = new ByteBuffer(window.AddRunData);

            InstructionDecoder instrDecoder = new InstructionDecoder(instructionBuffer, customTable);

            VCDiffResult result = VCDiffResult.SUCCESS;

            while (Decoded < window.DecodedDeltaLength && instructionBuffer.CanRead)
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

            if (window.HasChecksum)
            {
                uint adler = Checksum.ComputeAdler32(targetData.ToArray());

                if (adler != window.Checksum)
                {
                    result = VCDiffResult.ERROR;
                }
            }

            targetData.SetLength(0);
            return result;
        }

        private VCDiffResult DecodeCopy(int size, byte mode, ByteBuffer addresses)
        {
            long here = window.SourceLength + Decoded;
            long decoded = addressCache.DecodeAddress(here, mode, addresses);

            switch ((VCDiffResult)decoded)
            {
                case VCDiffResult.ERROR:
                    return VCDiffResult.ERROR;

                case VCDiffResult.EOD:
                    return VCDiffResult.EOD;

                default:
                    if (decoded < 0 || decoded > here)
                    {
                        return VCDiffResult.ERROR;
                    }
                    break;
            }

            if (decoded + size > window.SourceLength) return VCDiffResult.ERROR;
            dict.Position = decoded;
            var rbytes = dict.ReadBytes(size).Span;
            sout.Write(rbytes);
            targetData.Write(rbytes.ToArray());
            Decoded += size;
            return VCDiffResult.SUCCESS;

            // will come back to this once
            // target data reading is implemented
           /*if(decoded < window.SourceLength)
           {
                long partial = window.SourceLength - decoded;
                dict.Position = decoded;
                sout.writeBytes(dict.ReadBytes((int)partial));
                bytesWritten += partial;
                size -= (int)partial;
           }

            decoded -= window.SourceLength;

            while(size > (bytesDecoded - decoded))
            {
                long partial = bytesDecoded - decoded;
                target.Position = decoded;
                sout.writeBytes(target.ReadBytes((int)partial));
                decoded += partial;
                size -= (int)partial;
                bytesWritten += partial;
            }

            target.Position = decoded;
            sout.writeBytes(target.ReadBytes(size));*/

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

            for (int i = 0; i < size; i++)
            {
                sout.Write(b);
                targetData.WriteByte(b);
            }

            Decoded += size;

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

            var rbytes = addRun.ReadBytes(size).Span;
            sout.Write(rbytes);
            targetData.Write(rbytes);
            Decoded += size;
            return VCDiffResult.SUCCESS;
        }

        public void Dispose()
        {
            targetData.Dispose();
        }
    }
}