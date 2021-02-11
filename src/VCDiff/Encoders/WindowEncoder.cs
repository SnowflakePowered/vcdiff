using System;
using System.IO;
using System.Runtime.CompilerServices;
using VCDiff.Includes;
using VCDiff.Shared;

namespace VCDiff.Encoders
{
    internal class WindowEncoder : IDisposable
    {
        private int maxMode;
        private long dictionarySize;
        private long targetLength;
        private CodeTable table;
        private int lastOpcodeIndex;
        private AddressCache addrCache;
        private InstructionMap instrMap;
        private MemoryStream instructionAndSizes;
        private MemoryStream dataForAddAndRun;
        private MemoryStream addressForCopy;

        public ChecksumFormat ChecksumFormat { get; }

        public bool IsInterleaved { get; }

        public uint Checksum { get; }

        //This is a window encoder for the VCDIFF format
        //if you are not including a checksum simply pass 0 to checksum
        //it will be ignored
        public WindowEncoder(long dictionarySize, uint checksum, ChecksumFormat checksumFormat, bool interleaved)
        {
            this.Checksum = checksum;
            this.ChecksumFormat = checksumFormat;
            this.IsInterleaved = interleaved;
            this.dictionarySize = dictionarySize;

            // The encoder currently doesn't support encoding with a custom table
            // will be added in later since it will be easy as decoding is already implemented
            maxMode = AddressCache.DefaultLast;
            table = CodeTable.DefaultTable;
            addrCache = new AddressCache();
            targetLength = 0;
            lastOpcodeIndex = -1;
            instrMap = new InstructionMap();

            //Separate buffers for each type if not interleaved
            if (!interleaved)
            {
                instructionAndSizes = new MemoryStream();
                dataForAddAndRun = new MemoryStream();
                addressForCopy = new MemoryStream();
            }
            else
            {
                instructionAndSizes = dataForAddAndRun = addressForCopy = new MemoryStream();
            }
        }

#if NETCOREAPP3_1 || NET5_0
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
        private void EncodeInstruction(VCDiffInstructionType inst, int size, byte mode = 0)
        {
            if (lastOpcodeIndex >= 0)
            {
                int lastOp = instructionAndSizes.GetBuffer()[lastOpcodeIndex];

                int compoundOp;
                if (size <= byte.MaxValue)
                {
                    compoundOp = instrMap.LookSecondOpcode((byte)lastOp, (byte)inst, (byte)size, mode);
                    if (compoundOp != CodeTable.kNoOpcode)
                    {
                        instructionAndSizes.GetBuffer()[lastOpcodeIndex] = (byte)compoundOp;
                        lastOpcodeIndex = -1;
                        return;
                    }
                }

                compoundOp = instrMap.LookSecondOpcode((byte)lastOp, (byte)inst, 0, mode);
                if (compoundOp != CodeTable.kNoOpcode)
                {
                    instructionAndSizes.GetBuffer()[lastOpcodeIndex] = (byte)compoundOp;
                    //append size to instructionAndSizes
                    VarIntBE.AppendInt32(size, instructionAndSizes);
                    lastOpcodeIndex = -1;
                }
            }

            int opcode;
            if (size <= byte.MaxValue)
            {
                opcode = instrMap.LookFirstOpcode((byte)inst, (byte)size, mode);

                if (opcode != CodeTable.kNoOpcode)
                {
                    instructionAndSizes.WriteByte((byte)opcode);
                    lastOpcodeIndex = (int)instructionAndSizes.Length - 1;
                    return;
                }
            }
            opcode = instrMap.LookFirstOpcode((byte)inst, 0, mode);
            if (opcode == CodeTable.kNoOpcode)
            {
                return;
            }

            instructionAndSizes.WriteByte((byte)opcode);
            lastOpcodeIndex = (int)instructionAndSizes.Length - 1;
            VarIntBE.AppendInt32(size, instructionAndSizes);
        }

        public void Add(ReadOnlySpan<byte> data)
        {
            EncodeInstruction(VCDiffInstructionType.ADD, data.Length);
            dataForAddAndRun.Write(data);
            targetLength += data.Length;
        }

        public void Copy(int offset, int length)
        {
            byte mode = addrCache.EncodeAddress(offset, dictionarySize + targetLength, out long encodedAddr);
            EncodeInstruction(VCDiffInstructionType.COPY, length, mode);
            if (addrCache.WriteAddressAsVarint(mode))
            {
                VarIntBE.AppendInt64(encodedAddr, addressForCopy);
            }
            else
            {
                addressForCopy.WriteByte((byte)encodedAddr);
            }
            targetLength += length;
        }

        public void Run(int size, byte b)
        {
            EncodeInstruction(VCDiffInstructionType.RUN, size);
            dataForAddAndRun.WriteByte(b);
            targetLength += size;
        }

        private int CalculateLengthOfTheDeltaEncoding()
        {
            if (IsInterleaved)
            {
                return VarIntBE.CalcInt32Length((int)targetLength) +
                1 +
                VarIntBE.CalcInt32Length(0) +
                VarIntBE.CalcInt32Length((int)instructionAndSizes.Length) +
                VarIntBE.CalcInt32Length(0) +
                0 +
                (int)instructionAndSizes.Length
                // interleaved implies SDCH checksum if any.
                + (this.ChecksumFormat == ChecksumFormat.SDCH ? VarIntBE.CalcInt64Length(Checksum) : 0);
            }

            int lengthOfDelta = VarIntBE.CalcInt32Length((int)targetLength) +
            1 +
            VarIntBE.CalcInt32Length((int)dataForAddAndRun.Length) +
            VarIntBE.CalcInt32Length((int)instructionAndSizes.Length) +
            VarIntBE.CalcInt32Length((int)addressForCopy.Length) +
            (int)dataForAddAndRun.Length +
            (int)instructionAndSizes.Length +
            (int)addressForCopy.Length;

            if (this.ChecksumFormat == ChecksumFormat.SDCH)
            {
                lengthOfDelta += VarIntBE.CalcInt64Length(Checksum);
            }
            else if (this.ChecksumFormat == ChecksumFormat.Xdelta3)
            {
                lengthOfDelta += 4;
            }

            return lengthOfDelta;

        }

        public void Output(Stream outputStream)
        {
            int lengthOfDelta = CalculateLengthOfTheDeltaEncoding();
            int windowSize = lengthOfDelta +
            1 +
            VarIntBE.CalcInt32Length((int)dictionarySize) +
            VarIntBE.CalcInt32Length(0);
            VarIntBE.CalcInt32Length(lengthOfDelta);

            //Google's Checksum Implementation Support
            if (this.ChecksumFormat != ChecksumFormat.None)
            {
                outputStream.WriteByte((byte)VCDiffWindowFlags.VCDSOURCE | (byte)VCDiffWindowFlags.VCDCHECKSUM); //win indicator
            }
            else
            {
                outputStream.WriteByte((byte)VCDiffWindowFlags.VCDSOURCE); //win indicator
            }
            VarIntBE.AppendInt32((int)dictionarySize, outputStream); //dictionary size
            VarIntBE.AppendInt32(0, outputStream); //dictionary start position 0 is default aka encompass the whole dictionary

            VarIntBE.AppendInt32(lengthOfDelta, outputStream); //length of delta

            //begin of delta encoding
            long sizeBeforeDelta = outputStream.Position;
            VarIntBE.AppendInt32((int)targetLength, outputStream); //final target length after decoding
            outputStream.WriteByte(0x00); // uncompressed

            // [Here is where a secondary compressor would be used
            //  if the encoder and decoder supported that feature.]

            //non interleaved then it is separeat areas for each type
            if (!IsInterleaved)
            {
                VarIntBE.AppendInt32((int)dataForAddAndRun.Length, outputStream); //length of add/run
                VarIntBE.AppendInt32((int)instructionAndSizes.Length, outputStream); //length of instructions and sizes
                VarIntBE.AppendInt32((int)addressForCopy.Length, outputStream); //length of addresses for copys

                switch (this.ChecksumFormat)
                {
                    //Google Checksum Support
                    case ChecksumFormat.SDCH:
                        VarIntBE.AppendInt64(this.Checksum, outputStream);
                        break;
                    // Xdelta checksum support.
                    case ChecksumFormat.Xdelta3:
                    {
                        Span<byte> checksumBytes = stackalloc [] {
                            (byte)(this.Checksum >> 24), (byte)(this.Checksum >> 16), (byte)(this.Checksum >> 8), (byte)(this.Checksum & 0x000000FF) };
                        outputStream.Write(checksumBytes);
                        break;
                    }
                }

                outputStream.Write(dataForAddAndRun.GetBuffer().AsSpan(0, (int)dataForAddAndRun.Length)); //data section for adds and runs
                outputStream.Write(instructionAndSizes.GetBuffer().AsSpan(0, (int)instructionAndSizes.Length)); //data for instructions and sizes
                outputStream.Write(addressForCopy.GetBuffer().AsSpan(0, (int)addressForCopy.Length)); //data for addresses section copys
            }
            else
            {
                //interleaved everything is woven in and out in one block
                VarIntBE.AppendInt32(0, outputStream); //length of add/run
                VarIntBE.AppendInt32((int)instructionAndSizes.Length, outputStream); //length of instructions and sizes + other data for interleaved
                VarIntBE.AppendInt32(0, outputStream); //length of addresses for copys

                //Google Checksum Support
                if (this.ChecksumFormat == ChecksumFormat.SDCH)
                {
                    VarIntBE.AppendInt64(Checksum, outputStream);
                }

                outputStream.Write(instructionAndSizes.GetBuffer().AsSpan(0, (int)instructionAndSizes.Length)); //data for instructions and sizes, in interleaved it is everything
            }
            //end of delta encoding

            long sizeAfterDelta = outputStream.Position;
            if (lengthOfDelta != sizeAfterDelta - sizeBeforeDelta)
            {
                throw new IOException("Delta output length does not match");
            }
            dataForAddAndRun.SetLength(0);
            instructionAndSizes.SetLength(0);
            addressForCopy.SetLength(0);
            if (targetLength == 0)
            {
                throw new IOException("Empty target window");
            }
            addrCache = new AddressCache();
        }

        public void Dispose()
        {
            instructionAndSizes.Dispose();
            dataForAddAndRun.Dispose();
            addressForCopy.Dispose();
        }
    }
}