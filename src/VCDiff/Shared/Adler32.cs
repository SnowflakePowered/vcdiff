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

                    // Shuffling 2301 then 1032 achieves the same thing as described here.
                    // https://stackoverflow.com/questions/6996764/fastest-way-to-do-horizontal-float-vector-sum-on-x86
                    // Vector128<uint> hi64 = Sse2.Shuffle(v_s1, S1O32);
                    // Vector128<uint> sum64 = Sse2.Add(hi64, v_s1);
                    // Vector128<uint> hi32 = Sse2.ShuffleLow(sum64.AsUInt16(), S1O32).AsUInt32();
                    // Vector128<uint> sum32 = Sse2.Add(sum64, hi32);

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
                        s1 += buffAddr[dof++];
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

        private static unsafe uint HashAvx2(uint adler, ReadOnlySpan<byte> buff)
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

                    Vector256<sbyte> tap = Vector256.Create(32, 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20,
                        19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1);
                    Vector256<byte> zero = Vector256.Create((byte)0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                    Vector256<short> ones = Vector256.Create(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);

                    //  Process n blocks of data. At most NMAX data bytes can be processed before s2 must be reduced modulo BASE.
                    Vector256<uint> v_ps = Vector256.Create(0, 0, 0, 0, 0, 0, 0, s1 * n);
                    Vector256<uint> v_s2 = Vector256.Create(0, 0, 0, 0, 0, 0, 0, s2);
                    Vector256<uint> v_s1 = Vector256.Create(0u, 0, 0, 0, 0, 0, 0, 0);

                    do
                    {
                        // Load 32 input bytes.
                        var bytes = Avx2.LoadVector256((buffAddr + dof));

                        // Add previous block byte sum to v_ps. 
                        v_ps = Avx2.Add(v_ps, v_s1);

                        // Horizontally add the bytes for s1, multiply-adds the bytes by[32, 31, 30, ... ] for s2.
                        v_s1 = Avx2.Add(v_s1, Avx2.SumAbsoluteDifferences(bytes, zero).AsUInt32());
                        Vector256<short> mad = Avx2.MultiplyAddAdjacent(bytes, tap);
                        v_s2 = Avx2.Add(v_s2, Avx2.MultiplyAddAdjacent(mad, ones).AsUInt32());

                        dof += BLOCK_SIZE;
                    } while (--n > 0);

                    v_s2 = Avx2.Add(v_s2, Avx2.ShiftLeftLogical(v_ps, 5));

                    //  Sum epi32 ints v_s1(s2) and accumulate in s1(s2).

                    Vector128<uint> v128_s1 = Sse2.Add(Avx2.ExtractVector128(v_s1, 0), Avx2.ExtractVector128(v_s1, 1));
                    v128_s1 = Sse2.Add(v128_s1, Sse2.Shuffle(v128_s1, S23O1));
                    v128_s1 = Sse2.Add(v128_s1, Sse2.Shuffle(v128_s1, S1O32));
                    s1 += Sse2.ConvertToUInt32(v128_s1);

                    //// sum v_s2
                    Vector128<uint> v128_s2 = Sse2.Add(Avx2.ExtractVector128(v_s2, 0), Avx2.ExtractVector128(v_s2, 1));
                    v128_s2 = Sse2.Add(v128_s2, Sse2.Shuffle(v128_s2, S23O1));
                    v128_s2 = Sse2.Add(v128_s2, Sse2.Shuffle(v128_s2, S1O32));
                    s2 = Sse2.ConvertToUInt32(v128_s2);

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
                        s1 += buffAddr[dof++];
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
            uint len = (uint)buff.Length;
            if (len == 0) return 1;
            if (len == 1)
            {
                uint sum2 = adler >> 16 & 0xffff;
                adler &= 0xffff;

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

            if (len < 16)
            {
                uint sum2 = adler >> 16 & 0xffff;
                adler &= 0xffff;

                for (int i = 0; i < len; i++)
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

#if NETCOREAPP3_1
            if (Avx2.IsSupported) return Adler32.HashAvx2(adler, buff);
            if (Ssse3.IsSupported) return Adler32.HashSsse3(adler, buff);
#endif
            unsafe
            {
                fixed (byte* bufPtr = buff)
                {
                    uint sum2 = adler >> 16 & 0xffff;
                    adler &= 0xffff;

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
                            adler += bufPtr[dof++];
                            sum2 += adler;
                        }

                        adler %= BASE;
                        sum2 %= BASE;
                    }

                    return adler | (sum2 << 16);
                }
            }
        }
    }
}