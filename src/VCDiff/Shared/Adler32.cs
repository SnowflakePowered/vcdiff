using System;
using System.Numerics;

namespace VCDiff.Shared
{
    internal class Adler32
    {
        /// <summary>
        /// Zlib implementation of the Adler32 Hash
        /// </summary>
        private const uint BASE = 65521;

        private const uint NMAX = 5552;

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

        public static uint Hash(uint adler, ReadOnlySpan<byte> buff)
        {
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