using System;

namespace VCDiff.Benchmark
{
    /// <summary>
    /// Generates random data.
    /// </summary>
    public static class RandomDataGenerator
    {
        public static byte[] GetRandomBytes(int size)
        {
            var data = new byte[size];
            var random = new Random(size);
            for (int x = 0; x < data.Length; x++)
                data[x] = (byte)random.Next();

            return data;
        }
    }
}
