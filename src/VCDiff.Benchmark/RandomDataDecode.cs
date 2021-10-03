using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using VCDiff.Decoders;
using VCDiff.Encoders;

namespace VCDiff.Benchmark
{
    [MemoryDiagnoser()]
    [SimpleJob(1, 3, 8)]
    public class RandomDataDecode
    {
        [Params(//1 * 1024 * 256,  // 0.25MiB
            //1 * 1024 * 1024, // 2 MiB
            16 * 1024 * 1024 // 16 MiB
        )]
        public int Bytes { get; set; }

        [Params(32)] // AVX Only
        //[Params(16, 32)] // AVX vs SSE
        public int BlockSize { get; set; }

        private const int RepeatCount = 128;
        private byte[] _data;
        private Stream _patchSlightModified;
        private Stream _patchHeavyModified;

        private Stream _sourceStream;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _data = RandomDataGenerator.GetRandomBytes(Bytes);
            RandomDataEncode.MakeRandomData(_data, out var dataSlightModified, out var dataHeavyModified);

            // Make Source Stream
            _sourceStream = new MemoryStream(_data);

            // Make Slightly Mod Patch
            CreatePatch(dataSlightModified, ref _patchSlightModified);
            CreatePatch(dataHeavyModified, ref _patchHeavyModified);
        }

        private void CreatePatch(byte[] targetData, ref Stream receiver)
        {
            _sourceStream.Seek(0, SeekOrigin.Begin);
            receiver = new MemoryStream(_data.Length);
            using var targetStream = new MemoryStream(targetData);
            using var encoder = new VcEncoder(_sourceStream, targetStream, receiver, 1, BlockSize);
            encoder.Encode();
        }
        
        [Benchmark]
        public void DecodeSlightlyModified()
        {
            using var result = new MemoryStream((int)_sourceStream.Length);
            for (int x = 0; x < RepeatCount; x++)
            {
                _sourceStream.Seek(0, SeekOrigin.Begin);
                _patchSlightModified.Seek(0, SeekOrigin.Begin);
                result.Position = 0;
                using var decoder = new VcDecoder(_sourceStream, _patchSlightModified, result, int.MaxValue);
                decoder.Decode(out long written);
            }
        }

        [Benchmark]
        public void DecodeHeavilyModified()
        {
            using var result = new MemoryStream((int)_sourceStream.Length);
            for (int x = 0; x < RepeatCount; x++)
            {
                _sourceStream.Seek(0, SeekOrigin.Begin);
                _patchHeavyModified.Seek(0, SeekOrigin.Begin);
                result.Position = 0;
                using var decoder = new VcDecoder(_sourceStream, _patchHeavyModified, result, int.MaxValue);
                decoder.Decode(out long written);
            }
        }
    }
}
