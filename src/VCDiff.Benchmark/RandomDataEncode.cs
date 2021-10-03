using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using VCDiff.Encoders;

namespace VCDiff.Benchmark
{
    [MemoryDiagnoser()]
    [SimpleJob(1, 3, 8)]
    public class RandomDataEncode
    {
        [Params(//1 * 1024 * 256,  // 0.25MiB
                //1 * 1024 * 1024, // 2 MiB
                16 * 1024 * 1024 // 16 MiB
                )]
        public int Bytes { get; set; }

        [Params(32)] // AVX Only
        //[Params(16, 32)] // AVX vs SSE
        public int BlockSize { get; set; }

        private byte[] _data;
        private byte[] _dataSlightModified;
        private byte[] _dataHeavyModified;

        private Stream _sourceStream;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _data = RandomDataGenerator.GetRandomBytes(Bytes);
            MakeRandomData(_data, out _dataSlightModified, out _dataHeavyModified);
            _sourceStream = new MemoryStream(_data);
        }

        [IterationSetup]
        public void IterationSetup() => _sourceStream.Seek(0, SeekOrigin.Begin);

        [Benchmark]
        public void EncodeSlightlyModified()
        {
            using var targetStream = new MemoryStream(_dataSlightModified);
            using var patchStream  = new MemoryStream(_data.Length);

            using var encoder = new VcEncoder(_sourceStream, targetStream, patchStream, 1, BlockSize);
            encoder.Encode();
        }

        [Benchmark]
        public void EncodeHeavilyModified()
        {
            using var targetStream = new MemoryStream(_dataHeavyModified);
            using var patchStream  = new MemoryStream(_data.Length);

            using var encoder = new VcEncoder(_sourceStream, targetStream, patchStream, 1, BlockSize);
            encoder.Encode();
        }


        // Utility Methods
        public static void MakeRandomData(byte[] data, out byte[] dataSlightModified, out byte[] dataHeavyModified)
        {
            dataSlightModified = (byte[])data.Clone();
            dataHeavyModified = (byte[])data.Clone();

            var random = new Random(data.Length);
            for (int x = 0; x < dataHeavyModified.Length; x++)
            {
                var next = random.Next(0, 1000);
                if (next >= 250) // 3 / 4 chance.
                    dataHeavyModified[x] += (byte)next;
                if (next >= 995) // 1 / 200 chance.
                    dataSlightModified[x] += (byte)next;
            }
        }
    }
}
