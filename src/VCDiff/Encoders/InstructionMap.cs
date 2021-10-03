using System;
using VCDiff.Includes;
using VCDiff.Shared;

namespace VCDiff.Encoders
{
    internal class InstructionMap
    {
        private CodeTable table;
        private OpcodeMap firstMap;
        private OpcodeMap2 secondMap;

        /// <summary>
        /// Instruction mapping for op codes and such for using in encoding
        /// </summary>
        public unsafe InstructionMap()
        {
            table = CodeTable.DefaultTable;
            var inst2 = table.inst2;
            var inst1 = table.inst1;
            var size2 = table.size2;
            var size1 = table.size1;
            var mode1 = table.mode1;
            var mode2 = table.mode2;

            // max sizes are known for the default code table (18 and 6 respectively).
            firstMap = new OpcodeMap((int)VCDiffInstructionType.LAST + AddressCache.DefaultLast + 1, FindMaxSize(size1.AsSpan(), 18));
            secondMap = new OpcodeMap2((int)VCDiffInstructionType.LAST + AddressCache.DefaultLast + 1, FindMaxSize(size2.AsSpan(), 6));
            
            for (int opcode = 0; opcode < CodeTable.kCodeTableSize; ++opcode)
            {
                if (inst2.Pointer[opcode] == CodeTable.N)
                {
                    firstMap.Add(inst1.Pointer[opcode], size1.Pointer[opcode], mode1.Pointer[opcode], (byte)opcode);
                }
                else if (inst1.Pointer[opcode] == CodeTable.N)
                {
                    firstMap.Add(inst1.Pointer[opcode], size1.Pointer[opcode], mode1.Pointer[opcode], (byte)opcode);
                }
            }

            for (int opcode = 0; opcode < CodeTable.kCodeTableSize; ++opcode)
            {
                if ((inst1.Pointer[opcode] != CodeTable.N) && (inst2.Pointer[opcode] != CodeTable.N))
                {
                    int found = LookFirstOpcode(inst1.Pointer[opcode], size1.Pointer[opcode], mode1.Pointer[opcode]);
                    if (found == CodeTable.kNoOpcode) continue;
                    secondMap.Add((byte)found, inst2.Pointer[opcode], size2.Pointer[opcode], mode2.Pointer[opcode], (byte)opcode);
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

        private static byte FindMaxSize(ReadOnlySpan<byte> sizes, sbyte knownMaxSize = -1)
        {
            if (knownMaxSize > -1) return (byte)knownMaxSize;
            byte maxSize = sizes[0];
            int len = sizes.Length;
            for (int i = 1; i < len; i++)
            {
                if (maxSize < sizes[i])
                {
                    maxSize = sizes[i];
                }
            }
            return maxSize;
        }

        private struct OpcodeMap2
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
                    instmode = new int[numInstAndModes][];
                    opcodes2[opcode] = instmode;
                }
                int[] sizeArray = instmode[inst + mode];
                if (sizeArray == null)
                {
                    sizeArray = NewSizeOpcodeArray(maxSize + 1);
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
                Array.Fill(nn, CodeTable.kNoOpcode);
                return nn;
            }

            public int LookUp(byte first, byte inst, byte size, byte mode)
            {
                if (size > maxSize)
                {
                    return CodeTable.kNoOpcode;
                }

                int[][] instmode = opcodes2[first];
                if (instmode == null)
                {
                    return CodeTable.kNoOpcode;
                }

                int instModePointer = inst == CodeTable.C ? (inst + mode) : inst;
                return instmode[instModePointer]?[size] ?? CodeTable.kNoOpcode;
            }
        }

        private struct OpcodeMap
        {
            private int[] opcodes;
            private int maxSize;
            private int numInstAndModes;

            public OpcodeMap(int numInstAndModes, int maxSize)
            {
                this.maxSize = maxSize + 1;
                this.numInstAndModes = numInstAndModes;
                opcodes = new int[numInstAndModes * this.maxSize];

                Array.Fill(opcodes, CodeTable.kNoOpcode);
            }

            public void Add(byte inst, byte size, byte mode, byte opcode)
            {
                if (opcodes[(inst + mode) + numInstAndModes * size] == CodeTable.kNoOpcode)
                {
                    opcodes[(inst + mode) + numInstAndModes * size] = opcode;
                }
            }

            public int LookUp(byte inst, byte size, byte mode)
            {
                int instMode = (inst == CodeTable.C) ? (inst + mode) : inst;

                if (size > maxSize - 1)
                {
                    return CodeTable.kNoOpcode;
                }

                return opcodes[instMode + numInstAndModes * size];
            }
        }
    }
}