using System;

namespace VCDiff.Encoders
{
    internal class RollingHash
    {
        private const ulong kMult = 257;
        private const ulong kBase = (1 << 23);

        private ulong[] removeTable;
        private ulong multiplier;

        /// <summary>
        /// Rolling Hash Constructor
        /// </summary>
        /// <param name="size">block size</param>
        public RollingHash(int size)
        {
            removeTable = new ulong[256];
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
        }

        /// <summary>
        /// Generate a new hash from the bytes
        /// </summary>
        /// <param name="bytes">The bytes to generate the hash for</param>
        /// <returns></returns>
        public ulong Hash(Memory<byte> bytes)
        {
            long len = bytes.Length;
            Span<byte> span = bytes.Span;
            if (len == 0) return 1;
            if (len == 1) return span[0] * kMult;
            ulong h = (span[0] * kMult) + span[1];
            for (int i = 2; i < bytes.Length; i++)
            {
                h = (h * kMult + span[i]) & (kBase - 1);
            }

            return h;
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
    }
}