using System;
using VCDiff.Includes;
using VCDiff.Shared;

namespace VCDiff.Encoders
{
    public class InstructionMap
    {
        private CodeTable table;
        private OpcodeMap firstMap;
        private OpcodeMap2 secondMap;

        /// <summary>
        /// Instruction mapping for op codes and such for using in encoding
        /// </summary>
        public InstructionMap()
        {
            table = CodeTable.DefaultTable;
            firstMap = new OpcodeMap((int)VCDiffInstructionType.LAST + AddressCache.DefaultLast + 1, FindMaxSize(table.size1.Span));
            secondMap = new OpcodeMap2((int)VCDiffInstructionType.LAST + AddressCache.DefaultLast + 1, FindMaxSize(table.size2.Span));
            for (int opcode = 0; opcode < CodeTable.kCodeTableSize; ++opcode)
            {
                if (table.inst2.Span[opcode] == CodeTable.N)
                {
                    firstMap.Add(table.inst1.Span[opcode], table.size1.Span[opcode], table.mode1.Span[opcode], (byte)opcode);
                }
                else if (table.inst1.Span[opcode] == CodeTable.N)
                {
                    firstMap.Add(table.inst1.Span[opcode], table.size1.Span[opcode], table.mode1.Span[opcode], (byte)opcode);
                }
            }

            for (int opcode = 0; opcode < CodeTable.kCodeTableSize; ++opcode)
            {
                if ((table.inst1.Span[opcode] != CodeTable.N) && (table.inst2.Span[opcode] != CodeTable.N))
                {
                    int found = this.LookFirstOpcode(table.inst1.Span[opcode], table.size1.Span[opcode], table.mode1.Span[opcode]);
                    if (found == CodeTable.kNoOpcode) continue;
                    secondMap.Add((byte)found, table.inst2.Span[opcode], table.size2.Span[opcode], table.mode2.Span[opcode], (byte)opcode);
                }
            }
        }

        public int LookFirstOpcode(byte inst, byte size, byte mode)
        {
            return firstMap.LookUp(inst, size, mode);
        }

        public int LookSecondOpcode(byte first, byte inst, byte size, byte mode)
        {
            return secondMap.LookUp(first, inst, size, mode);
        }

        private static byte FindMaxSize(ReadOnlySpan<byte> sizes)
        {
            byte maxSize = sizes[0];
            for (int i = 1; i < sizes.Length; i++)
            {
                if (maxSize < sizes[i])
                {
                    maxSize = sizes[i];
                }
            }
            return maxSize;
        }

        private class OpcodeMap2
        {
            private int[][][] opcodes2;
            private int maxSize;
            private int numInstAndModes;

            public OpcodeMap2(int numInstAndModes, int maxSize)
            {
                this.maxSize = maxSize;
                this.numInstAndModes = numInstAndModes;
                opcodes2 = new int[CodeTable.kCodeTableSize][][];
            }

            public void Add(byte first, byte inst, byte size, byte mode, byte opcode)
            {
                int[][] instmode = opcodes2[first];

                if (instmode == null)
                {
                    instmode = new int[this.numInstAndModes][];
                    opcodes2[opcode] = instmode;
                }
                int[] sizeArray = instmode[inst + mode];
                if (sizeArray == null)
                {
                    sizeArray = NewSizeOpcodeArray(this.maxSize + 1);
                    instmode[inst + mode] = sizeArray;
                }
                if (sizeArray[size] == CodeTable.kNoOpcode)
                {
                    sizeArray[size] = opcode;
                }
            }

            private int[] NewSizeOpcodeArray(int size)
            {
                int[] nn = new int[size];
                for (int i = 0; i < size; ++i)
                {
                    nn[i] = CodeTable.kNoOpcode;
                }
                return nn;
            }

            public int LookUp(byte first, byte inst, byte size, byte mode)
            {
                if (size > this.maxSize)
                {
                    return CodeTable.kNoOpcode;
                }

                int[][] instmode = opcodes2[first];
                if (instmode == null)
                {
                    return CodeTable.kNoOpcode;
                }
                int instModePointer = (inst == CodeTable.C) ? (inst + mode) : inst;
                int[] sizeArray = instmode[instModePointer];
                if (sizeArray == null)
                {
                    return CodeTable.kNoOpcode;
                }
                return sizeArray[size];
            }
        }

        private class OpcodeMap
        {
            private int[,] opcodes;
            private int maxSize;
            private int numInstAndModes;

            public OpcodeMap(int numInstAndModes, int maxSize)
            {
                this.maxSize = maxSize;
                this.numInstAndModes = numInstAndModes;
                opcodes = new int[numInstAndModes, maxSize + 1];

                for (int i = 0; i < numInstAndModes; ++i)
                {
                    for (int j = 0; j < maxSize + 1; j++)
                    {
                        opcodes[i, j] = CodeTable.kNoOpcode;
                    }
                }
            }

            public void Add(byte inst, byte size, byte mode, byte opcode)
            {
                if (opcodes[inst + mode, size] == CodeTable.kNoOpcode)
                {
                    opcodes[inst + mode, size] = opcode;
                }
            }

            public int LookUp(byte inst, byte size, byte mode)
            {
                int instMode = (inst == CodeTable.C) ? (inst + mode) : inst;

                if (size > maxSize)
                {
                    return CodeTable.kNoOpcode;
                }

                return opcodes[instMode, size];
            }
        }
    }
}