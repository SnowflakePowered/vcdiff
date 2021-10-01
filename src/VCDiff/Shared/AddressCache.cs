using VCDiff.Includes;

namespace VCDiff.Shared
{
    internal class AddressCache
    {
        /// <summary>
        /// The address cache implementation as described in the RFC doc.
        /// </summary>
        private const byte DefaultNearCacheSize = 4;

        private const byte DefaultSameCacheSize = 3;
        private byte nearSize;
        private byte sameSize;
        private long[] nearCache;
        private long[] sameCache;
        private int nextSlot;
        
        public byte FirstNear => (byte)VCDiffModes.FIRST;

        public byte FirstSame => (byte)(VCDiffModes.FIRST + nearSize);

        public byte Last => (byte)(FirstSame + sameSize - 1);

        public static byte DefaultLast => (byte)(VCDiffModes.FIRST + DefaultNearCacheSize + DefaultSameCacheSize - 1);

        public AddressCache(byte nearSize, byte sameSize)
        {
            this.sameSize = sameSize;
            this.nearSize = nearSize;
            nearCache = new long[nearSize];
            sameCache = new long[sameSize * 256];
            nextSlot = 0;
        }

        public AddressCache()
        {
            sameSize = DefaultSameCacheSize;
            nearSize = DefaultNearCacheSize;
            nearCache = new long[nearSize];
            sameCache = new long[sameSize * 256];
            nextSlot = 0;
        }

        private static bool IsSelfMode(byte mode)
        {
            return mode == (byte)VCDiffModes.SELF;
        }

        private static bool IsHereMode(byte mode)
        {
            return mode == (byte)VCDiffModes.HERE;
        }

        private bool IsNearMode(byte mode)
        {
            return (mode >= FirstNear) && (mode < FirstSame);
        }

        private bool IsSameMode(byte mode)
        {
            return (mode >= FirstSame) && (mode <= Last);
        }

        private static long DecodeSelfAddress(long encoded)
        {
            return encoded;
        }

        private static long DecodeHereAddress(long encoded, long here)
        {
            return here - encoded;
        }

        private long DecodeNearAddress(byte mode, long encoded)
        {
            return NearAddress(mode - FirstNear) + encoded;
        }

        private long DecodeSameAddress(byte mode, byte encoded)
        {
            return SameAddress(((mode - FirstSame) * 256) + encoded);
        }

        public bool WriteAddressAsVarint(byte mode)
        {
            return !IsSameMode(mode);
        }

        private long NearAddress(int pos)
        {
            return nearCache[pos];
        }

        private long SameAddress(int pos)
        {
            return sameCache[pos];
        }

        private void UpdateCache(long address)
        {
            if (nearSize > 0)
            {
                nearCache[nextSlot] = address;
                nextSlot = (nextSlot + 1) % nearSize;
            }
            if (sameSize > 0)
            {
                sameCache[(int)(address % (sameSize * 256))] = address;
            }
        }

        public byte EncodeAddress(long address, long here, out long encoded)
        {
            if (address < 0)
            {
                encoded = 0;
                return 0;
            }
            if (address >= here)
            {
                encoded = 0;
                return 0;
            }

            if (sameSize > 0)
            {
                int pos = (int)(address % (sameSize * 256));
                if (SameAddress(pos) == address)
                {
                    UpdateCache(address);
                    encoded = (pos % 256);
                    return (byte)(FirstSame + (pos / 256));
                }
            }

            byte bestMode = (byte)VCDiffModes.SELF;
            long bestEncoded = address;

            long hereEncoded = here - address;
            if (hereEncoded < bestEncoded)
            {
                bestMode = (byte)VCDiffModes.HERE;
                bestEncoded = hereEncoded;
            }

            for (int i = 0; i < nearSize; ++i)
            {
                long nearEncoded = address - NearAddress(i);
                if ((nearEncoded >= 0) && (nearEncoded < bestEncoded))
                {
                    bestMode = (byte)(FirstNear + i);
                    bestEncoded = nearEncoded;
                }
            }

            UpdateCache(address);
            encoded = bestEncoded;
            return bestMode;
        }

        private bool IsDecodedAddressValid(long decoded, long here)
        {
            if (decoded < 0)
            {
                return false;
            }

            if (decoded >= here)
            {
                return false;
            }

            return true;
        }

        public long DecodeAddress(long here, byte mode, ByteBuffer sin)
        {
            long start = sin.Position;
            if (here < 0)
            {
                return (int)VCDiffResult.ERROR;
            }

            if (!sin.CanRead)
            {
                return (int)VCDiffResult.EOD;
            }

            long decoded = 0;
            if (IsSameMode(mode))
            {
                byte encoded = sin.ReadByte();
                decoded = DecodeSameAddress(mode, encoded);
            }
            else
            {
                int encoded = VarIntBE.ParseInt32(sin);

                switch (encoded)
                {
                    case (int)VCDiffResult.ERROR:
                        return encoded;

                    case (int)VCDiffResult.EOD:
                        sin.Position = (int) start;
                        return encoded;
                }

                if (IsSelfMode(mode))
                {
                    decoded = DecodeSelfAddress(encoded);
                }
                else if (IsHereMode(mode))
                {
                    decoded = DecodeHereAddress(encoded, here);
                }
                else if (IsNearMode(mode))
                {
                    decoded = DecodeNearAddress(mode, encoded);
                }
                else
                {
                    return (int)VCDiffResult.ERROR;
                }
            }

            if (!IsDecodedAddressValid(decoded, here))
            {
                return (int)VCDiffResult.ERROR;
            }
            UpdateCache(decoded);
            return decoded;
        }
    }
}