# VCDIFF source matcher benchmark

The harness reports median encode time, patch size, and managed bytes allocated per encode (B/op). Each encode that can use COPY builds a fresh source index and writes the entire patch to an in-memory stream. Before timing each configuration, the harness decodes its patch and compares the result byte for byte with the target.

## Workloads

| Invocation | Inputs |
| --- | --- |
| No arguments | Five deterministic workloads: 24 shared bytes per 32, unrelated bytes, shuffled records, and two small dictionaries with one byte changed every 127 bytes. |
| `--real` | Two Moby-Dick editions (`pg15.txt` → `pg2701.txt`), then the Windows x64 SQLite CLI update from 3.46.0 → 3.46.1. |
| `SOURCE TARGET` | A pair of local files. The repository includes `src/VCDiff.Tests/patches/a.test` and `b.test`. |

The two small-edit cases exercise different index representations at all three block widths. The 2,040-byte source contains 63–255 complete blocks and uses one-byte index entries. The 65,536-byte source contains 2,048–8,192 complete blocks and uses two-byte entries.

The real workloads download pinned inputs on each invocation. SHA-256 verification, ZIP extraction, and input loading happen before timing. For SQLite the harness reads only the extracted `sqlite3.exe` from each official Windows x64 tools archive. See the [pinned book editions](https://github.com/israellot/DeltaSharp/tree/677ebb03d71bebe9dd71ac4e9575c137277c2988/src/Benchmarks/Data) and SQLite releases [3.46.0](https://sqlite.org/releaselog/3_46_0.html) and [3.46.1](https://sqlite.org/releaselog/3_46_1.html).

## Reproduce

Run from the repository root with the .NET 10 runtime and a compatible SDK. The default is bounded search with a 256-candidate cap. Pass `--exhaustive` first to select exhaustive search.

```sh
dotnet build -c Release src/VCDiff.MatcherBenchmark
export DOTNET_TieredCompilation=0
bench=src/VCDiff.MatcherBenchmark/bin/Release/net10.0/VCDiff.MatcherBenchmark.dll

dotnet "$bench"
dotnet "$bench" --real
dotnet "$bench" --exhaustive --real
dotnet "$bench" src/VCDiff.Tests/patches/a.test src/VCDiff.Tests/patches/b.test
```

Each run uses 1 MiB target windows and tests 8-, 16-, and 32-byte blocks with the corresponding minimum COPY length. Medians are taken over 21 samples after five warmup samples. When both inputs are at most 64 KiB, each sample averages 32 encodes to reduce timer noise. Other samples measure one encode.

Allocation measurements use `GC.GetAllocatedBytesForCurrentThread` around synchronous encodes. They capture newly allocated managed memory, including output buffers. Native memory and storage already available in the warmed array pool are excluded. B/op measures allocation churn, not peak memory use.

[Measured results for all seven workloads](results-2026-11-25.csv) were collected on an Apple M3 Max using .NET 10.0.3, Release, with tiered compilation disabled.

## Benchmark the defaults

Pass `--defaults` first to run the configuration shown in the [README table](../../README.md#default-encode-performance). The harness constructs `VcEncoder` with `BlockSize.Bytes16` and leaves every optional setting at its default.

```sh
dotnet build -c Release src/VCDiff.MatcherBenchmark
export DOTNET_TieredCompilation=0
bench=src/VCDiff.MatcherBenchmark/bin/Release/net10.0/VCDiff.MatcherBenchmark.dll
dotnet "$bench" --defaults
dotnet "$bench" --defaults --real
dotnet "$bench" --defaults src/VCDiff.Tests/patches/a.test src/VCDiff.Tests/patches/b.test
```

The [default measurements](default-encode-2026-11-25.csv) cover all eight workloads. Each value for time and allocation is the median of three independent process medians, using the warmup and sample counts above. The index-capacity column gives the rented array’s full payload in bytes, including spare slots.

## Benchmark decoding

Pass `--decode` first to time patch application. Use `--decode --defaults --real` for the published workloads or `--decode --defaults SOURCE TARGET` for local files.

```sh
DOTNET_TieredCompilation=0 dotnet run -c Release --project src/VCDiff.MatcherBenchmark -- --decode --defaults --real
```

The harness creates each patch before timing. Each timed operation constructs and disposes a decoder and applies the complete patch to a reused, preallocated output stream. Both paths read the patch through a stream. The Stream case also reads the dictionary through a stream. The ByteBuffer case borrows an in-memory dictionary. After each decode the harness verifies the status, byte count, and reconstructed output.

| Workload | Dictionary API | Target bytes | Patch bytes | Median decode (ms) | Allocated bytes/decode |
| --- | --- | ---: | ---: | ---: | ---: |
| Moby-Dick | Stream | 1,253,974 | 35,197 | 0.1738 | 13,800 |
| Moby-Dick | ByteBuffer | 1,253,974 | 35,197 | 0.1687 | 13,752 |
| SQLite CLI 3.46.0 → 3.46.1 | Stream | 3,773,952 | 810,691 | 1.9753 | 27,448 |
| SQLite CLI 3.46.0 → 3.46.1 | ByteBuffer | 3,773,952 | 810,691 | 1.8637 | 27,400 |

Measured on an Apple M3 Max, .NET 10.0.3, Release, tiered compilation disabled. Times and managed allocations are medians of three process medians, each taken over 21 individual decodes after five warmups. Both inputs, the output backing array, and the warmed pool are outside the allocation measurement.

## GitHub-hosted x64 measurements

The [x64 workflow](../../.github/workflows/benchmarks.yml) runs the default encode workloads and both real-world decode workloads on four independent `ubuntu-24.04` runners. Each job records the CPU model, OS image, and exact .NET runtime. The runner label specifies the architecture; GitHub assigns the host CPU.

The [November 25 run](https://github.com/israellot/vcdiff/actions/runs/45624111232) used four-vCPU Ubuntu 24.04.5 VMs, .NET 10.0.15, Release builds, and disabled tiered compilation. Each reported value is the median of three process medians on that VM. The harness verifies the decoded target outside the timed region. The [main README table](../../README.md#default-encode-performance) uses the lowest-numbered sample for each CPU vendor: [Intel Xeon Platinum 8573C, sample 3](https://github.com/israellot/vcdiff/actions/runs/45624111232/job/139239671984) and [AMD EPYC 9V74, sample 1](https://github.com/israellot/vcdiff/actions/runs/45624111232/job/139239672477). The [complete x64 measurements](github-actions-x64-2026-11-25.csv) include all four VMs, the second EPYC 9V74 sample, the EPYC 9V45 sample, and managed allocations per operation.

Use **Actions → x64 benchmarks → Run workflow** when the workflow is on the default branch. Open the resulting job artifacts for environment details and individual process medians.
