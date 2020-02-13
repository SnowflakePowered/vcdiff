using System;
using System.Buffers;
using System.Numerics;

#if NETCOREAPP3_1
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif
namespace VCDiff.Encoders
{
    public class RollingHash : IDisposable
    {
        private const byte S23O1 = (((2) << 6) | ((3) << 4) | ((0) << 2) | ((1)));
        private const byte S1O32 = (((1) << 6) | ((0) << 4) | ((3) << 2) | ((2)));

        private const int kMult = 257;
        private const int kBase = (1 << 23);

        private ulong[] removeTable;
        private int[] kMultFactors;
        private MemoryHandle kMultFactorsHandle;
        private unsafe int* kMultFactorsPtr;
        private ulong multiplier;

        /// <summary>
        /// Rolling Hash Constructor
        /// </summary>
        /// <param name="size">block size</param>
        public RollingHash(int size)
        {
            this.WindowSize = size;
            removeTable = new ulong[256];
            kMultFactors = new int[size];
            kMultFactorsHandle = kMultFactors.AsMemory().Pin();
            unsafe
            {
                kMultFactorsPtr = (int *)kMultFactorsHandle.Pointer;
            }
            multiplier = 1;

            for (int i = 0; i < size - 1; ++i)
            {
                multiplier = (multiplier * kMult) & (kBase - 1);
            }
            ulong byteTimes = 0;
            for (int i = 0; i < 256; ++i)
            {
                // Get the inverse of the modBase
                removeTable[i] = (0 - byteTimes) & (kBase - 1);
                byteTimes = (byteTimes + multiplier) & (kBase - 1);
            }

            uint c = 1;
            for (int i = 0; i < size; i++)
            {
                kMultFactors[i] = (int)c;
                c = (c * kMult) & (kBase - 1);
            }
        }

        public int WindowSize { get; }

        ulong FastIntegerPower(ulong x, int exp)
        {
            ulong result = 1;
            for (; ; )
            {
                if ((exp & 1) != 0)
                    result *= x;
                exp >>= 1;
                if (exp == 0)
                    break;
                x *= x;
            }

            return result;
        }

#if NETCOREAPP3_1
        private unsafe ulong HashAvx2(Span<byte> span)
        {
            int len = span.Length;
            if (len == 0) return 1;
            if (len == 1) return span[0] * (uint)kMult;
            ulong h = 0;
            Vector256<int> v_ps = Vector256<int>.Zero;
            Vector256<int> v_kbase = Vector256.Create(kBase - 1);
            Vector256<int> v_shuf = Vector256.Create(7, 6, 5, 4, 3, 2, 1, 0);
            fixed (byte* _buf = span)
            {
                int* buf = (int*) _buf;
                for (int i = 0, j = len - i - 1; len - i >= 8; i += 8, j = len - i - 1)
                {

                    var x = kMultFactorsPtr[j - 7];
                    Vector256<int> c_v = Avx2.LoadDquVector256(&kMultFactorsPtr[j - 7]);
                    c_v = Avx2.PermuteVar8x32(c_v, v_shuf);
                    
                    Vector256<int> s_v = Vector256.Create(span[i + 0], span[i + 1], span[i + 2],
                        span[i + 3], span[i + 4],
                        span[i + 5], span[i + 6], span[i + 7]);

                    v_ps = Avx2.Add(v_ps, Avx2.And(Avx2.MultiplyLow(c_v, s_v), v_kbase));
                }
            }


            Vector128<int> v128_s1 = Sse2.Add(Avx2.ExtractVector128(v_ps, 0), Avx2.ExtractVector128(v_ps, 1));
            v128_s1 = Sse2.Add(v128_s1, Sse2.Shuffle(v128_s1, S23O1));
            v128_s1 = Sse2.Add(v128_s1, Sse2.Shuffle(v128_s1, S1O32));
            h += Sse2.ConvertToUInt32(v128_s1.AsUInt32());

            return h & (kBase - 1);
        }
#endif
        /// <summary>
        /// Generate a new hash from the bytes
        ///
        /// The formula for calculating h is
        /// h(0) = 1
        /// h(n) = SUM {i=0}^{n-1} c^{n - i - 1} S[i]
        ///
        /// where n is the length of S, and c is kMult.
        ///
        /// In code,
        /// h(n) = Sum(i: 0, n: len - 1, i => kMult ** (len - i - 1) span[i])
        ///
        /// The final result is then MODded using binary and with kBase.
        ///
        /// </summary>
        /// <param name="bytes">The bytes to generate the hash for</param>
        /// <returns></returns>
        public ulong Hash(Memory<byte> bytes)
        {
            int len = bytes.Length;
            Span<byte> span = bytes.Span;
            if (len == 0) return 1;
            if (len == 1) return span[0] * (uint)kMult;

#if NETCOREAPP3_1
            if (Avx2.IsSupported && len >= 8) return HashAvx2(span);
#endif
            ulong h = 0;

            //// Old Version 
            //ulong hi = (span[0] * (uint)kMult) + span[1];

            //for (int j = 2; j < len; j++)
            //{
            //    hi = ((hi * kMult) + span[j]) & (kBase - 1);
            //}

            //// equivalent version
            //int i = 0;
            //int vecLength = Vector<ulong>.Count;
            //for (; len - i > vecLength; i += vecLength)
            //{
            //    int index = len - i - 1;
            //    var v_bytes = new Vector<byte>(span.Slice(i));
            //    var v_factors = new Vector<ulong>(kMultFactors, index - vecLength);
            //    var x = Vector.Multiply(v_factors, Vector.AsVectorUInt64(v_bytes));
            //    h += Vector.Dot(x, Vector<ulong>.One);
            //}

            for (int i = 0; i < len; i++)
            {
                int index = len - i - 1;
                ulong c = (uint)kMultFactors[index];
                h += c * span[i];
            }

            ulong yy = h & (kBase - 1);
            return h & (kBase - 1);
        }

        /// <summary>
        /// Rolling update for the hash
        /// First byte must be the first bytee that was used in the data
        /// that was last encoded
        /// new byte is the first byte position + Size
        /// </summary>
        /// <param name="oldHash">the original hash</param>
        /// <param name="firstByte">the original byte of the data for the first hash</param>
        /// <param name="newByte">the first byte of the new data to hash</param>
        /// <returns></returns>
        public ulong UpdateHash(ulong oldHash, byte firstByte, byte newByte)
        {
            // Remove the first byte from the hash
            ulong partial = (oldHash + removeTable[firstByte]) & (kBase - 1);

            // Do the hash step
            return (partial * kMult + newByte) & (kBase - 1);
        }

        public void Dispose()
        {
            kMultFactorsHandle.Dispose();
        }
    }
}