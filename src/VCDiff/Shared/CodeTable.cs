using System;
using VCDiff.Includes;

namespace VCDiff.Shared
{
    internal class CodeTable : IDisposable
    {
        /// <summary>
        /// Default CodeTable as described in the RFC doc
        /// </summary>
        public static readonly int kNoOpcode = 0x100;

        public static readonly int kCodeTableSize = 256;

        public byte[] table = new byte[kCodeTableSize * 6];

        public NativeAllocation<byte> inst1;
        public NativeAllocation<byte> inst2;
        public NativeAllocation<byte> size1;
        public NativeAllocation<byte> size2;
        public NativeAllocation<byte> mode1;
        public NativeAllocation<byte> mode2;

        public const byte N = (byte)VCDiffInstructionType.NOOP;
        public const byte A = (byte)VCDiffInstructionType.ADD;
        public const byte R = (byte)VCDiffInstructionType.RUN;
        public const byte C = (byte)VCDiffInstructionType.COPY;
        public const byte ERR = (byte)VCDiffInstructionType.ERROR;
        public const byte EOD = (byte)VCDiffInstructionType.EOD;

        private static readonly byte[] defaultInst1 = {
                R,  // opcode 0
                A, A, A, A, A, A, A, A, A, A, A, A, A, A, A, A, A, A,  // opcodes 1-18
                C, C, C, C, C, C, C, C, C, C, C, C, C, C, C, C,  // opcodes 19-34
                C, C, C, C, C, C, C, C, C, C, C, C, C, C, C, C,  // opcodes 35-50
                C, C, C, C, C, C, C, C, C, C, C, C, C, C, C, C,  // opcodes 51-66
                C, C, C, C, C, C, C, C, C, C, C, C, C, C, C, C,  // opcodes 67-82
                C, C, C, C, C, C, C, C, C, C, C, C, C, C, C, C,  // opcodes 83-98
                C, C, C, C, C, C, C, C, C, C, C, C, C, C, C, C,  // opcodes 99-114
                C, C, C, C, C, C, C, C, C, C, C, C, C, C, C, C,  // opcodes 115-130
                C, C, C, C, C, C, C, C, C, C, C, C, C, C, C, C,  // opcodes 131-146
                C, C, C, C, C, C, C, C, C, C, C, C, C, C, C, C,  // opcodes 147-162
                A, A, A, A, A, A, A, A, A, A, A, A,  // opcodes 163-174
                A, A, A, A, A, A, A, A, A, A, A, A,  // opcodes 175-186
                A, A, A, A, A, A, A, A, A, A, A, A,  // opcodes 187-198
                A, A, A, A, A, A, A, A, A, A, A, A,  // opcodes 199-210
                A, A, A, A, A, A, A, A, A, A, A, A,  // opcodes 211-222
                A, A, A, A, A, A, A, A, A, A, A, A,  // opcodes 223-234
                A, A, A, A,  // opcodes 235-238
                A, A, A, A,  // opcodes 239-242
                A, A, A, A,  // opcodes 243-246
                C, C, C, C, C, C, C, C, C  // opcodes 247-255
            };

        private static readonly byte[] defaultInst2 = {
                N,  // opcode 0
                N, N, N, N, N, N, N, N, N, N, N, N, N, N, N, N, N, N,  // opcodes 1-18
                N, N, N, N, N, N, N, N, N, N, N, N, N, N, N, N,  // opcodes 19-34
                N, N, N, N, N, N, N, N, N, N, N, N, N, N, N, N,  // opcodes 35-50
                N, N, N, N, N, N, N, N, N, N, N, N, N, N, N, N,  // opcodes 51-66
                N, N, N, N, N, N, N, N, N, N, N, N, N, N, N, N,  // opcodes 67-82
                N, N, N, N, N, N, N, N, N, N, N, N, N, N, N, N,  // opcodes 83-98
                N, N, N, N, N, N, N, N, N, N, N, N, N, N, N, N,  // opcodes 99-114
                N, N, N, N, N, N, N, N, N, N, N, N, N, N, N, N,  // opcodes 115-130
                N, N, N, N, N, N, N, N, N, N, N, N, N, N, N, N,  // opcodes 131-146
                N, N, N, N, N, N, N, N, N, N, N, N, N, N, N, N,  // opcodes 147-162
                C, C, C, C, C, C, C, C, C, C, C, C,  // opcodes 163-174
                C, C, C, C, C, C, C, C, C, C, C, C,  // opcodes 175-186
                C, C, C, C, C, C, C, C, C, C, C, C,  // opcodes 187-198
                C, C, C, C, C, C, C, C, C, C, C, C,  // opcodes 199-210
                C, C, C, C, C, C, C, C, C, C, C, C,  // opcodes 211-222
                C, C, C, C, C, C, C, C, C, C, C, C,  // opcodes 223-234
                C, C, C, C,  // opcodes 235-238
                C, C, C, C,  // opcodes 239-242
                C, C, C, C,  // opcodes 243-246
                A, A, A, A, A, A, A, A, A  // opcodes 247-255
    };

        private static readonly byte[] defaultSize1 = {
                0,  // opcode 0
                0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17,  // 1-18
                0, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18,  // 19-34
                0, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18,  // 35-50
                0, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18,  // 51-66
                0, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18,  // 67-82
                0, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18,  // 83-98
                0, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18,  // 99-114
                0, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18,  // 115-130
                0, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18,  // 131-146
                0, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18,  // 147-162
                1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4,  // opcodes 163-174
                1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4,  // opcodes 175-186
                1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4,  // opcodes 187-198
                1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4,  // opcodes 199-210
                1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4,  // opcodes 211-222
                1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4,  // opcodes 223-234
                1, 2, 3, 4,  // opcodes 235-238
                1, 2, 3, 4,  // opcodes 239-242
                1, 2, 3, 4,  // opcodes 243-246
                4, 4, 4, 4, 4, 4, 4, 4, 4  // opcodes 247-255
            };

        private static readonly byte[] defaultSize2 = {
                0,  // opcode 0
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 1-18
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 19-34
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 35-50
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 51-66
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 67-82
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 83-98
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 99-114
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 115-130
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 131-146
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 147-162
                4, 5, 6, 4, 5, 6, 4, 5, 6, 4, 5, 6,  // opcodes 163-174
                4, 5, 6, 4, 5, 6, 4, 5, 6, 4, 5, 6,  // opcodes 175-186
                4, 5, 6, 4, 5, 6, 4, 5, 6, 4, 5, 6,  // opcodes 187-198
                4, 5, 6, 4, 5, 6, 4, 5, 6, 4, 5, 6,  // opcodes 199-210
                4, 5, 6, 4, 5, 6, 4, 5, 6, 4, 5, 6,  // opcodes 211-222
                4, 5, 6, 4, 5, 6, 4, 5, 6, 4, 5, 6,  // opcodes 223-234
                4, 4, 4, 4,  // opcodes 235-238
                4, 4, 4, 4,  // opcodes 239-242
                4, 4, 4, 4,  // opcodes 243-246
                1, 1, 1, 1, 1, 1, 1, 1, 1  // opcodes 247-255
            };

        private static readonly byte[] defaultMode1 = {
                0,  // opcode 0
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 1-18
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 19-34
                1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,  // opcodes 35-50
                2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,  // opcodes 51-66
                3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,  // opcodes 67-82
                4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4,  // opcodes 83-98
                5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5,  // opcodes 99-114
                6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6,  // opcodes 115-130
                7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,  // opcodes 131-146
                8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,  // opcodes 147-162
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 163-174
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 175-186
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 187-198
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 199-210
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 211-222
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 223-234
                0, 0, 0, 0,  // opcodes 235-238
                0, 0, 0, 0,  // opcodes 239-242
                0, 0, 0, 0,  // opcodes 243-246
                0, 1, 2, 3, 4, 5, 6, 7, 8  // opcodes 247-255
            };

        private static readonly byte[] defaultMode2 = {
                0,  // opcode 0
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 1-18
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 19-34
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 35-50
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 51-66
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 67-82
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 83-98
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 99-114
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 115-130
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 131-146
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 147-162
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  // opcodes 163-174
                1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,  // opcodes 175-186
                2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,  // opcodes 187-198
                3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,  // opcodes 199-210
                4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4,  // opcodes 211-222
                5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5,  // opcodes 223-234
                6, 6, 6, 6,  // opcodes 235-238
                7, 7, 7, 7,  // opcodes 239-242
                8, 8, 8, 8,  // opcodes 243-246
                0, 0, 0, 0, 0, 0, 0, 0, 0  // opcodes 247-255
            };

        public static CodeTable DefaultTable = new CodeTable();

        public CodeTable()
        {
            InitTableSegment(0, defaultInst1, ref inst1);
            InitTableSegment(1, defaultInst2, ref inst2);

            InitTableSegment(2, defaultSize1, ref size1);
            InitTableSegment(3, defaultSize2, ref size2);

            InitTableSegment(4, defaultMode1, ref mode1);
            InitTableSegment(5, defaultMode2, ref mode2);
        }

        ~CodeTable()
        {
            Dispose();
        }

        private void InitTableSegment(int row, byte[] defaultBytes, ref NativeAllocation<byte> alloc)
        {
            var rowSpan = table.AsSpan(row * kCodeTableSize, kCodeTableSize);
            alloc = new NativeAllocation<byte>(rowSpan.Length);
            defaultBytes.CopyTo(alloc.AsSpan());
        }

        public bool SetBytes(byte[] items)
        {
            if (items.Length != kCodeTableSize * 6)
            {
                return false;
            }

            items.CopyTo(table, 0);
            return true;
        }

        public ByteBuffer GetBytes()
        {
            return new ByteBuffer(table);
        }

        public void Dispose()
        {
            inst1.Dispose();
            inst2.Dispose();
            size1.Dispose();
            size2.Dispose();
            mode1.Dispose();
            mode2.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}