using System;
using BenchmarkDotNet.Running;

namespace VCDiff.Benchmark
{
    class Program
    {
        static void Main(string[] args)
        {
            BenchmarkRunner.Run<RandomDataDecode>();
            BenchmarkRunner.Run<RandomDataEncode>();
        }
    }
}
