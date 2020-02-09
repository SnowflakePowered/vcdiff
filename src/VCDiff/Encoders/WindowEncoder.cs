using System;
using System.Collections.Generic;
using System.IO;
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

        public bool HasChecksum { get; }

        public bool IsInterleaved { get; }

        public uint Checksum { get; }

        //This is a window encoder for the VCDIFF format
        //if you are not including a checksum simply pass 0 to checksum
        //it will be ignored
        public WindowEncoder(long dictionarySize, uint checksum, bool interleaved = false, bool hasChecksum = false)
        {
            Checksum = checksum;
            HasChecksum = hasChecksum;
            IsInterleaved = interleaved;
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

        private void EncodeInstruction(VCDiffInstructionType inst, int size, byte mode = 0)
        {
            if (lastOpcodeIndex >= 0)
            {
                int lastOp = instructionAndSizes.GetBuffer()[lastOpcodeIndex];

                if (inst == VCDiffInstructionType.ADD && (table.inst1.Span[lastOp] == CodeTable.A))
                {
                    //warning adding two in a row
                    Console.WriteLine("Warning: performing two ADD instructions in a row.");
                }
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
            int extraLength = 0;

            if (HasChecksum)
            {
                extraLength += VarIntBE.CalcInt64Length(Checksum);
            }

            if (!IsInterleaved)
            {
                int lengthOfDelta = VarIntBE.CalcInt32Length((int)targetLength) +
                1 +
                VarIntBE.CalcInt32Length((int)dataForAddAndRun.Length) +
                VarIntBE.CalcInt32Length((int)instructionAndSizes.Length) +
                VarIntBE.CalcInt32Length((int)addressForCopy.Length) +
                (int)dataForAddAndRun.Length +
                (int)instructionAndSizes.Length +
                (int)addressForCopy.Length;

                lengthOfDelta += extraLength;

                return lengthOfDelta;
            }
            else
            {
                int lengthOfDelta = VarIntBE.CalcInt32Length((int)targetLength) +
                1 +
                VarIntBE.CalcInt32Length(0) +
                VarIntBE.CalcInt32Length((int)instructionAndSizes.Length) +
                VarIntBE.CalcInt32Length(0) +
                0 +
                (int)instructionAndSizes.Length;

                lengthOfDelta += extraLength;

                return lengthOfDelta;
            }
        }

        public void Output(ByteStreamWriter sout)
        {
            int lengthOfDelta = CalculateLengthOfTheDeltaEncoding();
            int windowSize = lengthOfDelta +
            1 +
            VarIntBE.CalcInt32Length((int)dictionarySize) +
            VarIntBE.CalcInt32Length(0);
            VarIntBE.CalcInt32Length(lengthOfDelta);

            //Google's Checksum Implementation Support
            if (HasChecksum)
            {
                sout.Write((byte)VCDiffWindowFlags.VCDSOURCE | (byte)VCDiffWindowFlags.VCDCHECKSUM); //win indicator
            }
            else
            {
                sout.Write((byte)VCDiffWindowFlags.VCDSOURCE); //win indicator
            }
            VarIntBE.AppendInt32((int)dictionarySize, sout); //dictionary size
            VarIntBE.AppendInt32(0, sout); //dictionary start position 0 is default aka encompass the whole dictionary

            VarIntBE.AppendInt32(lengthOfDelta, sout); //length of delta

            //begin of delta encoding
            long sizeBeforeDelta = sout.Position;
            VarIntBE.AppendInt32((int)targetLength, sout); //final target length after decoding
            sout.Write(0x00); // uncompressed

            // [Here is where a secondary compressor would be used
            //  if the encoder and decoder supported that feature.]

            //non interleaved then it is separeat areas for each type
            if (!IsInterleaved)
            {
                VarIntBE.AppendInt32((int)dataForAddAndRun.Length, sout); //length of add/run
                VarIntBE.AppendInt32((int)instructionAndSizes.Length, sout); //length of instructions and sizes
                VarIntBE.AppendInt32((int)addressForCopy.Length, sout); //length of addresses for copys

                //Google Checksum Support
                if (HasChecksum)
                {
                    VarIntBE.AppendInt64(Checksum, sout);
                }

                sout.Write(dataForAddAndRun.GetBuffer().AsSpan(0, (int)dataForAddAndRun.Length)); //data section for adds and runs
                sout.Write(instructionAndSizes.GetBuffer().AsSpan(0, (int)instructionAndSizes.Length)); //data for instructions and sizes
                sout.Write(addressForCopy.GetBuffer().AsSpan(0, (int)addressForCopy.Length)); //data for addresses section copys
            }
            else
            {
                //interleaved everything is woven in and out in one block
                VarIntBE.AppendInt32(0, sout); //length of add/run
                VarIntBE.AppendInt32((int)instructionAndSizes.Length, sout); //length of instructions and sizes + other data for interleaved
                VarIntBE.AppendInt32(0, sout); //length of addresses for copys

                //Google Checksum Support
                if (HasChecksum)
                {
                    VarIntBE.AppendInt64(Checksum, sout);
                }

                sout.Write(instructionAndSizes.GetBuffer().AsSpan(0, (int)instructionAndSizes.Length)); //data for instructions and sizes, in interleaved it is everything
            }
            //end of delta encoding

            long sizeAfterDelta = sout.Position;
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