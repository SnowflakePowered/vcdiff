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
        private const byte SO123 = (((0) << 6) | ((1) << 4) | ((2) << 2) | ((3)));
        private const byte SOO2O = (((0) << 6) | ((0) << 4) | ((2) << 2) | ((0)));

        private const int kMult = 257;
        private const int kBase = (1 << 23);

        private ulong[] removeTable;
        private int[] kMultFactors;
        private MemoryHandle kMultFactorsHandle;
        private unsafe int* kMultFactorsPtr;
        private ulong multiplier;

#if NETCOREAPP3_1
        private readonly Vector128<int> v_kbase_sse;

        private readonly Vector256<int> v_kbase;
        private readonly Vector256<int> v_shuf;
#endif
        /// <summary>
        /// Rolling Hash Constructor
        /// </summary>
        /// <param name="size">block size</param>
        public RollingHash(int size)
        {

#if NETCOREAPP3_1
            v_kbase = Vector256.Create(kBase - 1);
            v_kbase_sse = Vector128.Create(kBase - 1);
            v_shuf = Vector256.Create(7, 6, 5, 4, 3, 2, 1, 0);
#endif
            this.WindowSize = size;
            removeTable = new ulong[256];
            kMultFactors = new int[size];
            kMultFactorsHandle = kMultFactors.AsMemory().Pin();
            unsafe
            {
                kMultFactorsPtr = (int*)kMultFactorsHandle.Pointer;
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
        private unsafe ulong HashAvx2(byte* buf, int len)
        {
            ulong h = 0;
            Vector256<int> v_ps = Vector256<int>.Zero;

            int i = 0;
            for (int j = len - i - 1; len - i >= 8; i += 8, j = len - i - 1)
            {
                Vector256<int> c_v = Avx.LoadDquVector256(&kMultFactorsPtr[j - 7]);
                c_v = Avx2.PermuteVar8x32(c_v, v_shuf);

                Vector128<byte> q_v = Sse2.LoadVector128(buf + i);
                Vector256<int> s_v = Avx2.ConvertToVector256Int32(q_v);

                v_ps = Avx2.Add(v_ps, Avx2.And(Avx2.MultiplyLow(c_v, s_v), v_kbase));
            }

            Vector128<int> v128_s1 = Sse2.Add(Avx2.ExtractVector128(v_ps, 0), Avx2.ExtractVector128(v_ps, 1));
            v128_s1 = Sse2.Add(v128_s1, Sse2.Shuffle(v128_s1, S23O1));
            v128_s1 = Sse2.Add(v128_s1, Sse2.Shuffle(v128_s1, S1O32));
            h += Sse2.ConvertToUInt32(v128_s1.AsUInt32());

            for (; i < len; i++)
            {
                int index = len - i - 1;
                ulong c = (uint)kMultFactors[index];
                h += c * buf[i];
            }

            return h & (kBase - 1);
        }

        private unsafe ulong HashSse(byte* buf, int len)
        {
            ulong h = 0;
            Vector128<int> v_ps = Vector128<int>.Zero;
            bool useSse4 = Sse41.IsSupported;

            int i = 0;
            for (int j = len - i - 1; len - i >= 4; i += 4, j = len - i - 1)
            {
                Vector128<int> c_v = Sse2.LoadVector128(&kMultFactorsPtr[j - 3]);
                c_v = Sse2.Shuffle(c_v, SO123);
                Vector128<byte> q_v = Sse2.LoadVector128(buf + i);

                Vector128<int> s_v;
                if (useSse4)
                {
                    s_v = Sse41.ConvertToVector128Int32(q_v);
                }
                else
                {
                    q_v = Sse2.UnpackLow(q_v, q_v);
                    s_v = Sse2.ShiftRightLogical(Sse2.UnpackLow(q_v.AsUInt16(), q_v.AsUInt16()).AsInt32(), 24);
                }

                if (useSse4)
                {
                    v_ps = Sse2.Add(v_ps, Sse2.And(Sse41.MultiplyLow(c_v, s_v), v_kbase_sse));
                }
                else
                {
                    
                    Vector128<ulong> v_tmp1 = Sse2.Multiply(c_v.AsUInt32(), s_v.AsUInt32());
                    Vector128<ulong> v_tmp2 =
                        Sse2.Multiply(Sse2.ShiftRightLogical128BitLane(c_v.AsByte(), 4).AsUInt32(),
                            Sse2.ShiftRightLogical128BitLane(s_v.AsByte(), 4).AsUInt32());
                    ;
                    v_ps = Sse2.Add(v_ps, Sse2.And(Sse2.UnpackLow(Sse2.Shuffle(v_tmp1.AsInt32(), SOO2O),
                        Sse2.Shuffle(v_tmp2.AsInt32(), SOO2O)), v_kbase_sse));
                }
            }

            v_ps = Sse2.Add(v_ps, Sse2.Shuffle(v_ps, S23O1));
            v_ps = Sse2.Add(v_ps, Sse2.Shuffle(v_ps, S1O32));
            h += Sse2.ConvertToUInt32(v_ps.AsUInt32());

            for (; i < len; i++)
            {
                int index = len - i - 1;
                ulong c = (uint)kMultFactors[index];
                h += c * buf[i];
            }

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
        internal unsafe ulong Hash(byte* buf, int len)
        {

            if (len == 0) return 1;
            if (len == 1) return buf[0] * (uint)kMult;


#if NETCOREAPP3_1
          
            if (Avx2.IsSupported && len >= 8) return HashAvx2(buf, len);
            if (Sse41.IsSupported && len >= 4) return HashSse(buf, len);
#endif
            ulong h = 0;

            //// Old Version 
            //ulong hi = (span[0] * (uint)kMult) + span[1];

            //for (int j = 2; j < len; j++)
            //{
            //    hi = ((hi * kMult) + span[j]) & (kBase - 1);
            //}

            for (int i = 0; i < len; i++)
            {
                int index = len - i - 1;
                ulong c = (uint)kMultFactors[index];
                h += c * buf[i];
            }


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