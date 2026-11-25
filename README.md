# vcdiff

[![Nuget](https://img.shields.io/nuget/v/VCdiff)](https://www.nuget.org/packages/VCDiff)
[![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/SnowflakePowered/vcdiff/dotnet.yml)](https://github.com/SnowflakePowered/vcdiff/actions?query=workflow%3A.NET)
[![Codecov](https://img.shields.io/codecov/c/github/SnowflakePowered/vcdiff)](https://codecov.io/gh/SnowflakePowered/vcdiff/branch/master)

vcdiff is a .NET library for creating and applying VCDIFF patches, based on the original [C# implementation by Metric](https://github.com/Metric/VCDiff).

Support for [xdelta3](https://github.com/jmacd/xdelta) checksums have also been included. Testing was done with xdelta 3.1, support for xdelta 3.0 patch files has not been tested. Only patch files without external compression (`-S none`) are supported. 

|Format|Encoding|Decoding|
|------|--------|--------|
|RFC3284-compliant VCDIFF|✔️|✔️|
|SDCH with Adler32 Checksum|✔️|✔️|
|SDCH Interleaved (with and without Adler32 Checksum)|✔️|✔️|
|xdelta3 with Adler32 Checksum (without compression)|✔️|✔️|
|xdelta3 with Adler32 Checksum and `VCD_APPHEADER` (without compression)|❌|✔️|
|xdelta3 with external compression|❌|❌|

## Default encode performance

These measurements use the recommended `BlockSize.Bytes16` constructor with its default settings: a 16-byte minimum COPY, bounded search (256 candidates per target position), `IndexPreference.Speed`, and 1 MiB target windows.

| Workload | M3 Max (ms) | Xeon 8573C (ms) | EPYC 9V74 (ms) | Target bytes | Patch bytes | Warm allocation (M3 Max) | Index capacity |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Moby-Dick | 0.64 | 1.52 | 1.15 | 1,253,974 | 35,197 | 1.16 MiB | 2.00 MiB |
| SQLite CLI 3.46.0 → 3.46.1 | 14.11 | 23.95 | 19.59 | 3,773,952 | 810,691 | 6.05 MiB | 4.00 MiB |
| Repository binary fixture | 5.96 | 10.49 | 8.59 | 2,097,152 | 275,744 | 2.54 MiB | 2.00 MiB |
| Small edits (8-bit index) | 0.0025 | 0.0078 | 0.0084 | 2,040 | 99 | 9.81 KiB | 0.50 KiB |
| Small edits (16-bit index) | 0.0578 | 0.15 | 0.0695 | 65,536 | 2,605 | 85.20 KiB | 32.00 KiB |
| Shuffled records | 2.37 | 4.37 | 3.15 | 133,120 | 18,595 | 220.88 KiB | 64.00 KiB |
| 24 shared bytes per 32 | 1.06 | 2.01 | 1.63 | 262,144 | 98,240 | 662.65 KiB | 128.00 KiB |
| Unrelated bytes | 2.25 | 3.66 | 2.89 | 262,144 | 262,170 | 1.26 MiB | 128.00 KiB |

Times are median Release encodes. The Apple M3 Max ran locally on macOS with .NET 10.0.3. The Intel Xeon Platinum 8573C and AMD EPYC 9V74 ran on four-vCPU GitHub-hosted Ubuntu 24.04.5 VMs with .NET 10.0.15. VM contention and OS/runtime differences can affect these timings. Warm allocation counts managed bytes allocated during one encode after warmup. Index capacity is the full size of the rented source-index array and includes reusable storage. Each patch was decoded and checked against its target. [Apple measurements](src/VCDiff.MatcherBenchmark/default-encode-2026-11-25.csv) · [x64 timings and allocations](src/VCDiff.MatcherBenchmark/github-actions-x64-2026-11-25.csv) · [GitHub Actions run](https://github.com/israellot/vcdiff/actions/runs/45624111232) · [Reproduce the benchmark](src/VCDiff.MatcherBenchmark/README.md#benchmark-the-defaults)

## Supported targets

The library targets .NET Standard 2.0 and 2.1, .NET 6, and .NET 10.

## Encoding data

Use a readable dictionary stream positioned at its beginning, a seekable target stream, and a writable patch stream. The source dictionary can contain up to `int.MaxValue` bytes. The stream constructor copies it into encoder-owned memory. If the dictionary is already in a `ByteBuffer`, pass that buffer and keep it alive during encoding. The caller owns the supplied streams and buffers.

```csharp
using System;
using System.IO;
using VCDiff.Encoders;
using VCDiff.Includes;

using var dictionary = File.OpenRead("original.bin");
using var target = File.OpenRead("updated.bin");
using var patch = File.Create("update.vcdiff");
using var encoder = new VcEncoder(dictionary, target, patch, blockSize: BlockSize.Bytes16);
if (encoder.Encode() != VCDiffResult.SUCCESS)
{
    throw new InvalidOperationException("Encoding requires a nonempty dictionary and target.");
}
```

## Encoder configuration

For general use, start with `new VcEncoder(dictionary, target, patch, blockSize: BlockSize.Bytes16)`. This is the configuration in the [performance table](#default-encode-performance). Choose the block, minimum COPY length, and window size at construction. Set the search and index properties before calling `Encode` or `EncodeAsync`.

| Setting | C# usage | Effect |
| --- | --- | --- |
| Block size | `blockSize: BlockSize.Bytes16` | Each source block contributes one index entry. Choose `BlockSize.Bytes8` to find shorter matches or `BlockSize.Bytes32` to index fewer source positions. |
| Minimum COPY length | `chunkSize: 0` | Zero uses the block size. An explicit minimum must be at least the block size. If the target is shorter than this minimum, the encoder emits literal data and skips building the source index. |
| Target window size | `maxBufferSize: 1` | Measured in MiB. Larger windows allow longer COPY instructions and require more buffer space. Each window can search the entire source dictionary. |
| Search mode | `Matcher = MatchAlgorithm.Bounded` | Bounded search examines up to 256 candidates per target position. `MatchAlgorithm.Exhaustive` can find longer matches but can take more time on repetitive inputs. |
| Index sizing | `IndexPreference = IndexPreference.Speed` | For 16- and 32-bit index entries, `IndexPreference.Memory` rounds the block count up to a power of two. The speed setting doubles that bucket count. An 8-bit index always uses 256 buckets. |

For a dictionary with 40,000 complete blocks, the memory setting uses 65,536 buckets and the speed setting uses 131,072. Both work with either search mode. The decoder reads COPY offsets and lengths directly from the patch.

### Patch format and progress

These arguments apply to each call to `Encode` or `EncodeAsync`. The table shows their defaults.

| Setting | C# usage | Effect |
| --- | --- | --- |
| Interleaved output | `interleaved: false` | Standard VCDIFF stores literals, instructions, and addresses in separate sections. Pass `interleaved: true` for the SDCH interleaved layout. |
| Checksum format | `checksumFormat: ChecksumFormat.None` | Choose `ChecksumFormat.Xdelta3` or `ChecksumFormat.SDCH` for per-window checksums. Interleaved output supports `ChecksumFormat.None` and `ChecksumFormat.SDCH`. |
| Progress reporting | `progress: null` | Supply an `IProgress<float>` to receive the fraction of target bytes consumed after each window, from zero to one. |

This example creates an xdelta3-checksummed patch with a smaller source index. It keeps the 16-byte block width and raises the minimum COPY length to 32 bytes.

```csharp
using System;
using System.IO;
using VCDiff.Encoders;
using VCDiff.Includes;
using VCDiff.Shared;

using var dictionary = File.OpenRead("original.bin");
using var target = File.OpenRead("updated.bin");
using var patch = File.Create("update.vcdiff");
using var encoder = new VcEncoder(
    dictionary,
    target,
    patch,
    blockSize: BlockSize.Bytes16,
    chunkSize: 32)
{
    IndexPreference = IndexPreference.Memory
};

if (encoder.Encode(checksumFormat: ChecksumFormat.Xdelta3) != VCDiffResult.SUCCESS)
{
    throw new InvalidOperationException("Encoding requires a nonempty dictionary and target.");
}
```

### Legacy constructors

The integer constructors and `RollingHash` are deprecated and retained for binary compatibility. Use an overload that takes `BlockSize` and omit `rollingHash`. An integer block size rounds to the nearest of 8, 16, and 32, with ties going to the smaller width (12 → 8, 24 → 16, 48 → 32). A legacy `chunkSize` below 2 selects twice the rounded width. Other values are raised to the rounded width if needed. Thus a three-argument call uses 16/32, while an explicit `blockSize: BlockSize.Bytes16` uses 16/16. The optional `rollingHash` argument is ignored. To preserve the three-argument call’s settings, use `new VcEncoder(dictionary, target, patch, blockSize: BlockSize.Bytes16, chunkSize: 32)`.

## Decoding data

Pass the original dictionary and the VCDIFF patch to a `VcDecoder`. The decoder writes reconstructed bytes to the output stream. The optional `maxTargetFileSize` argument sets the limit **per target window**, in bytes (default `64 * 1024 * 1024`). If the encoder uses larger windows, raise this limit accordingly. The decoder verifies embedded checksums by default (`disableChecksums: false`). It stages each window in a pooled buffer and writes it to the destination only after decoding and checksum verification succeed. If a later window fails validation, earlier completed windows remain available and count toward `bytesWritten`. Both decode APIs honor a patch’s custom instruction table and address-cache sizes.

```csharp
using System.IO;
using VCDiff.Decoders;
using VCDiff.Includes;

using var dictionary = File.OpenRead("original.bin");
using var patch = File.OpenRead("update.vcdiff");
using var restored = File.Create("restored.bin");
using var decoder = new VcDecoder(dictionary, patch, restored);
if (decoder.Decode(out long bytesWritten) != VCDiffResult.SUCCESS)
{
    throw new InvalidDataException("The patch could not be decoded completely.");
}
```

`DecodeAsync()` returns a tuple containing the decode status and byte count. The caller owns the streams in both examples.

Default patches applied through the stream-based decoder on the systems listed above:

| Workload | M3 Max (ms) | Xeon 8573C (ms) | EPYC 9V74 (ms) |
| --- | ---: | ---: | ---: |
| Moby-Dick | 0.174 | 0.329 | 0.240 |
| SQLite CLI 3.46.0 → 3.46.1 | 1.975 | 3.353 | 2.847 |

[Decode benchmark method](src/VCDiff.MatcherBenchmark/README.md#benchmark-decoding) · [x64 results for both dictionary APIs](src/VCDiff.MatcherBenchmark/github-actions-x64-2026-11-25.csv)

## License

vcdiff is a derivative work of [open-vcdiff](https://github.com/google/open-vcdiff) and [xdelta3](https://github.com/jmacd/xdelta), and thus is also licensed under the [Apache License, Version 2.0](LICENSE).
