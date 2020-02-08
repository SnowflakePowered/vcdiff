namespace VCDiff.Shared
{
    public class Checksum
    {
        public static uint ComputeAdler32(byte[] buffer)
        {
            return Adler32.Hash(0, buffer);
        }

        public static long UpdateAdler32(uint partial, byte[] buffer)
        {
            return Adler32.Hash(partial, buffer);
        }
    }
}