# vcdiff

[![Nuget](https://img.shields.io/nuget/v/VCdiff)](https://www.nuget.org/packages/VCDiff)

This is a hard fork of [VCDiff](https://github.com/Metric/VCDiff), originally written by [Metric](https://github.com/Metric), written primarily for use in Snowflake.

Large chunks have been rewritten, and heavily optimized to be *extremely fast*, using vector intrinsics, as well as `Memory<byte>` and `Span<byte>` APIs to eke out every bit of performance possible. Non-scientific preliminary testing shows up to a 20x speedup compared to the original library when diffing a 2MB file. 

Support for [xdelta3](https://github.com/jmacd/xdelta) checksums have also been included. Testing was done with xdelta 3.1, support for xdelta 3.0 patch files has not been tested. Only patch files without external compression (`-S none`) are supported. 

|Format|Encoding|Decoding|
|------|--------|--------|
|RFC3284-compliant VCDIFF|✔️|✔️|
|SDHC with Adler32 Checksum|✔️|✔️|
|SDHC Interleaved (with and without Adler32 Checksum)|✔️|✔️|
|xdelta3 with Adler32 Checksum (without compression)|✔️|✔️|
|xdelta3 with Adler32 Checksum and `VCD_APPHEADER` (without compression)|❌|✔️|
|xdelta3 with external compression|❌|❌|

The Adler32 checksum implementation will use SIMD on systems that support SSSE3 or AVX2 extensions when used in a .NET Core 3.1 application. .NET Standard 2.1 is the minimum supported TFM, because of the reliance on System.Buffers.

<details><summary>The original readme, with some changes to the API usage examples</summary>
<p>

This is a full implementation of open-vcdiff in C# based on [Google's open-vcdiff](https://github.com/google/open-vcdiff). This is written entirely in C# - no external C++ libraries required. This includes proper SDHC support with interleaving and checksums. The only thing it does not support is encoding with a custom CodeTable currently. Will be added later if requested, or feel free to add it in and send a pull request.

It is fully compatible with Google's open-vcdiff for encoding and decoding. If you find any bugs please let me know. I tried to test as thoroughly as possible between this and Google's github version. The largest file I tested with was 10MB. Should be able to support up to 2-4GB depending on your system.

## Requirements
Vector intrinsics and the `Span<T>` and `Memory<T>` memory APIs require .netstandard 2.1.


# Encoding Data
The dictionary must be a file or data that is already in memory. The file must be fully read in first in order to encode properly. This is just how the algorithm works for VCDiff. The encode function is blocking.

```csharp
using VCDiff.Include;
using VCDiff.Encoders;
using VCDiff.Shared;

void DoEncode() {
    using(FileStream output = new FileStream("...some output path", FileMode.Create, FileAccess.Write))
    using(FileStream dict = new FileStream("..dictionary / old file path", FileMode.Open, FileAccess.Read))
    using(FileStream target = new FileStream("..target data / new data path", FileMode.Open, FileMode.Read)) {
        VcEncoder coder = new VcEncoder(dict, target, output);
        VCDiffResult result = coder.Encode(); //encodes with no checksum and not interleaved
        if(result != VCDiffResult.SUCCESS) {
            //error was not able to encode properly
        }
    }
}

```

Encoding with checksum or interleaved or both

```csharp
encoder.Encode(interleaved: true, checksum: false);
encoder.Encode(interleaved: true, checksum: true);
encoder.Encode(interleaved: false, checksum: true);
```

Modifying the default chunk size for windows

```csharp
int windowSize = 2; //in Megabytes. The default is 1MB window chunks.

VcEnoder coder = new VcEncoder(dict, target, output, windowSize)
```

Modifying the default minimum copy encode size. Which means the match must be >= MinBlockSize in order to qualify as match for copying from dictionary file.

```csharp
// chunkSize is the minimum copy encode size.
// Default is 32 bytes. Lowering this can improve the delta compression for small files. 
// It must be a power of 2. 
VcEncoder coder = new VcEncoder(dict, target, output, blockSize: 8, chunkSize: 16);
```

Modifying the default BlockSize for hashing

```csharp
// Increasing blockSize for large files with similar data can improve results.
VcEncoder coder = new VcEncoder(dict, target, output, blockSize: 32);
```

# Decoding Data
The dictionary must be a file or data that is already in memory. The file must be fully read in first in order to decode properly. 

Due note the interleaved version of a delta file is meant for streaming and it is supported by the decoder already. However, non-interleaved expects access for reading the full delta file at one time. The delta file is still streamed, but must be able to read fully in sequential order.

```csharp
using VCDiff.Include;
using VCDiff.Decoders;
using VCDiff.Shared;

void DoDecode() {
    using (FileStream output = new FileStream("...some output path", FileMode.Create, FileAccess.Write))
    using (FileStream dict = new FileStream("..dictionary / old file path", FileMode.Open, FileAccess.Read))
    using (FileStream target = new FileStream("..delta encoded part", FileMode.Open, FileMode.Read)) {
        VCDecoder decoder = new VCDecoder(dict, target, output);

        // The header of the delta file must be available before the first call to decoder.Decode().
        long bytesWritten = 0;
        result = decoder.Decode(out bytesWritten);

        if(result != VCDiffResult.SUCCESS) {
            //error decoding
        }

        // if success bytesWritten will contain the number of bytes that were decoded
    }
}
```

Handling streaming of the interleaved format has the same setup. But instead you will continue calling decode until you know you have received everything. So, you will need to keep track of that. Everytime you loop through make sure you have enough data in the buffer to at least be able to decode the next VCDiff Window Header (which can be up to 22 bytes or so). After that the decode function will handle the waiting for the next part of the interleaved data for that VCDiff Window. The decode function is blocking.

```csharp
while (bytesWritten < someSizeThatYouAreExpecting) {
    // make sure we have enough data in buffer to at least try and decode the next window section
    // otherwise we will probably receive an error.
    if(myStream.Length < 22) continue; 

    long thisChunk = 0;
    result = decoder.Decode(out thisChunk);

    bytesWritten += thisChunk;

    if (result == VCDiffResult.ERROR) {
        // it failed to decode something
        // could be an issue that the window failed to parse
        // or actual data failed to decode properly
        break;
    }

    // otherwise continue on if you get SUCCESS or EOD (End of Data);
    // because only you know when you will have the data finished loading
    // the decoder doesn't care if nothing is available and it will keep trying until more is
}
```

</p>
</details>

# License
vcdiff is a derivative work of [open-vcdiff](https://github.com/google/open-vcdiff) and [xdelta3](https://github.com/jmacd/xdelta), and thus is also licensed under the Apache Public License 2.0.
