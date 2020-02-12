using System;
#if NETCOREAPP3_1
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif

namespace VCDiff.Shared
{
    internal class Adler32
    {
        /// <summary>
        /// Zlib implementation of the Adler32 Hash
        /// </summary>
        private const uint BASE = 65521;

        private const uint NMAX = 5552;

        private const int BLOCK_SIZE = 1 << 5;

        private const byte S23O1 = (((2) << 6) | ((3) << 4) | ((0) << 2) | ((1)));
        private const byte S1O32 = (((1) << 6) | ((0) << 4) | ((3) << 2) | ((2)));

        // todo #define _MM_SHUFFLE(fp3,fp2,fp1,fp0) (((fp3) << 6) | ((fp2) << 4) | ((fp1) << 2) | ((fp0)))
        // #define S23O1 _MM_SHUFFLE(2,3,0,1)  /* A B C D -> B A D C */
        // #define S1O32 _MM_SHUFFLE(1,0,3,2)  /* A B C D -> C D A B */

        public static uint Combine(uint adler1, uint adler2, uint len)
        {
            uint sum1 = 0;
            uint sum2 = 0;
            uint rem = 0;

            rem = len % BASE;
            sum1 = adler1 & 0xffff;
            sum2 = rem * sum1;
            sum2 %= BASE;
            sum1 += (adler2 & 0xffff) + BASE - 1;
            sum2 += ((adler1 >> 16) & 0xffff) + ((adler2 >> 16) & 0xffff) + BASE - rem;
            if (sum1 >= BASE) sum1 -= BASE;
            if (sum1 >= BASE) sum1 -= BASE;
            if (sum2 >= (BASE << 1)) sum2 -= (BASE << 1);
            if (sum2 >= BASE) sum2 -= BASE;
            return sum1 | (sum2 << 16);
        }

        private static void Do(ref uint adler, ref uint sum2, ReadOnlySpan<byte> buffer, int i, int times)
        {
            while (times-- > 0)
            {
                adler += buffer[i++];
                sum2 += adler;
            }
        }

#if NETCOREAPP3_1
        /// <summary>
        /// SSSE3 Version of Adler32
        /// https://chromium.googlesource.com/chromium/src/third_party/zlib/+/master/adler32_simd.c
        /// </summary>
        /// <param name="adler"></param>
        /// <param name="buff"></param>
        /// <returns></returns>
        private static unsafe uint HashSsse3(uint adler, ReadOnlySpan<byte> buff)
        {
            fixed (byte* buffAddr = buff)
            {
                uint s1 = adler & 0xffff;
                uint s2 = adler >> 16;

                int dof = 0;
                int len = buff.Length;
                int blocks = len / BLOCK_SIZE;
                len -= blocks * BLOCK_SIZE;

                while (blocks > 0)
                {
                    uint n = NMAX / BLOCK_SIZE;
                    if (n > blocks) n = (uint)blocks;
                    blocks -= (int)n;

                    Vector128<sbyte> tap1 = Vector128.Create(32, 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 17);
                    Vector128<sbyte> tap2 = Vector128.Create(16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1);
                    Vector128<byte> zero = Vector128.Create((byte)0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                    Vector128<short> ones = Vector128.Create(1, 1, 1, 1, 1, 1, 1, 1);

                    //  Process n blocks of data. At most NMAX data bytes can be processed before s2 must be reduced modulo BASE.
                    Vector128<uint> v_ps = Vector128.Create(0, 0, 0, s1 * n);
                    Vector128<uint> v_s2 = Vector128.Create(0, 0, 0, s2);
                    Vector128<uint> v_s1 = Vector128.Create(0u, 0, 0, 0);

                    do
                    {
                        // Load 32 input bytes.
                        var bytes1 = Sse2.LoadVector128((buffAddr + dof));
                        var bytes2 = Sse2.LoadVector128((buffAddr + dof) + 16);


                        // Add previous block byte sum to v_ps. 
                        v_ps = Sse2.Add(v_ps, v_s1);

                        // Horizontally add the bytes for s1, multiply-adds the bytes by[32, 31, 30, ... ] for s2.
                        v_s1 = Sse2.Add(v_s1, Sse2.SumAbsoluteDifferences(bytes1, zero).AsUInt32());
                        Vector128<short> mad1 = Ssse3.MultiplyAddAdjacent(bytes1, tap1);
                        v_s2 = Sse2.Add(v_s2, Sse2.MultiplyAddAdjacent(mad1, ones).AsUInt32());
                        v_s1 = Sse2.Add(v_s1, Sse2.SumAbsoluteDifferences(bytes2, zero).AsUInt32());
                        var mad2 = Ssse3.MultiplyAddAdjacent(bytes2, tap2);
                        v_s2 = Sse2.Add(v_s2, Sse2.MultiplyAddAdjacent(mad2, ones).AsUInt32());
                        dof += BLOCK_SIZE;
                    } while (--n > 0);

                    v_s2 = Sse2.Add(v_s2, Sse2.ShiftLeftLogical(v_ps, 5));

                    //  Sum epi32 ints v_s1(s2) and accumulate in s1(s2).
                    v_s1 = Sse2.Add(v_s1, Sse2.Shuffle(v_s1, S23O1));
                    v_s1 = Sse2.Add(v_s1, Sse2.Shuffle(v_s1, S1O32));
                    s1 += Sse2.ConvertToUInt32(v_s1);
                    v_s2 = Sse2.Add(v_s2, Sse2.Shuffle(v_s2, S23O1));
                    v_s2 = Sse2.Add(v_s2, Sse2.Shuffle(v_s2, S1O32));
                    s2 = Sse2.ConvertToUInt32(v_s2);

                    // Reduce
                    s1 %= BASE;
                    s2 %= BASE;
                }

                // Handle leftover data
                if (len > 0)
                {
                    while (len >= 16)
                    {
                        len -= 16;
                        Do(ref s1, ref s2, buff, dof, 16);
                        dof += 16;
                    }
                    while (len-- > 0)
                    {
                        s1 += buff[dof++];
                        s2 += s1;
                    }

                    if (s1 >= BASE)
                        s1 -= BASE;
                    s2 %= BASE;
                }
                /*
                * Return the recombined sums.
                */
                return s1 | (s2 << 16);
            }
        }

#endif
        public static uint Hash(uint adler, ReadOnlySpan<byte> buff)
        {
#if NETCOREAPP3_1
            if (Ssse3.IsSupported) return Adler32.HashSsse3(adler, buff);
#endif
            if (buff.Length == 0) return 1;
            uint sum2 = adler >> 16 & 0xffff;
            adler &= 0xffff;

            if (buff.Length == 1)
            {
                adler += buff[0];
                if (adler >= BASE)
                {
                    adler -= BASE;
                }
                sum2 += adler;
                if (sum2 >= BASE)
                {
                    sum2 -= BASE;
                }
                return adler | (sum2 << 16);
            }

            if (buff.Length < 16)
            {
                for (int i = 0; i < buff.Length; i++)
                {
                    adler += buff[i];
                    sum2 += adler;
                }
                if (adler >= BASE)
                {
                    adler -= BASE;
                }
                sum2 %= BASE;
                return adler | (sum2 << 16);
            }

            uint len = (uint)buff.Length;
            int dof = 0;
            while (len >= NMAX)
            {
                len -= NMAX;
                uint n = NMAX / 16;
                do
                {
                    Do(ref adler, ref sum2, buff, dof, 16);
                    dof += 16;
                } while (--n > 0);
                adler %= BASE;
                sum2 %= BASE;
            }

            if (len > 0)
            {
                while (len >= 16)
                {
                    len -= 16;
                    Do(ref adler, ref sum2, buff, dof, 16);
                    dof += 16;
                }
                while (len-- > 0)
                {
                    adler += buff[dof++];
                    sum2 += adler;
                }
                adler %= BASE;
                sum2 %= BASE;
            }

            return adler | (sum2 << 16);
        }
    }
}