using System.Diagnostics;
using VCDiff.Decoders;
using VCDiff.Encoders;
using VCDiff.Includes;
using VCDiff.Shared;

bool benchmarkDecode = args.FirstOrDefault() == "--decode";
if (benchmarkDecode) args = args[1..];

bool defaults = args.FirstOrDefault() == "--defaults";
if (defaults) args = args[1..];

#if UPSTREAM_API
Console.WriteLine(defaults ? "Three-argument constructor defaults" : "Matched settings using upstream-compatible constructors");
#else
var candidate = MatchAlgorithm.Bounded;
if (!defaults && args.FirstOrDefault() is ("--bounded" or "--exhaustive"))
{
    if (args[0] == "--exhaustive") candidate = MatchAlgorithm.Exhaustive;
    args = args[1..];
}
Console.WriteLine(defaults ? "Recommended Bytes16 constructor defaults" : $"Search mode: {candidate}");
#endif
if (args is ["--real"])
{
    using var client = new HttpClient();
    var book = await RealWorldInputs.MobyDick(client);
    Compare("Moby-Dick (pg15 to pg2701)", book.Source, book.Target);
    var sqlite = await RealWorldInputs.SQLiteUpdate(client);
    Compare("SQLite CLI x64 (3.46.0 to 3.46.1)", sqlite.Source, sqlite.Target);
}
else if (args.Length == 2)
{
    Compare(Path.GetFileName(args[1]), File.ReadAllBytes(args[0]), File.ReadAllBytes(args[1]));
}
else if (args.Length == 0)
{
    var random = new Random(2026);
    byte[] source = new byte[256 * 1024];
    random.NextBytes(source);
    byte[] target = (byte[])source.Clone();
    for (int i = 24; i < target.Length; i += 32)
        random.NextBytes(target.AsSpan(i, 8));
    Compare("24 shared bytes per 32", source, target);
    random.NextBytes(target);
    Compare("Unrelated bytes", source, target);

    // Shared prefixes create long candidate chains. Separators prevent a COPY
    // from extending across records and consuming the next shared prefix.
    const int records = 2048, recordSize = 64;
    source = new byte[records * recordSize];
    random.NextBytes(source);
    for (int i = recordSize; i < source.Length; i += recordSize)
        source.AsSpan(0, 32).CopyTo(source.AsSpan(i, 32));
    int[] order = Enumerable.Range(0, records).ToArray();
    random.Shuffle(order);
    target = new byte[records * (recordSize + 1)];
    for (int i = 0; i < records; i++)
    {
        source.AsSpan(order[i] * recordSize, recordSize).CopyTo(target.AsSpan(i * (recordSize + 1)));
        target[i * (recordSize + 1) + recordSize] = (byte)(source[0] ^ 0xFF);
    }
    Compare("Shuffled records", source, target);

    CompareSmallEdits("Small edits (8-bit index)", 255 * 8);
    CompareSmallEdits("Small edits (16-bit index)", 64 * 1024);
}
else
{
    Console.Error.WriteLine("Supply source and target paths, use --real for Moby-Dick and SQLite, or omit both for synthetic inputs. Put --decode first to benchmark patch application.");
    return 1;
}
return 0;

void CompareSmallEdits(string name, int size)
{
    var random = new Random(2026);
    var source = new byte[size];
    random.NextBytes(source);
    var target = (byte[])source.Clone();
    for (int i = 63; i < size; i += 127)
        target[i] ^= 0xFF;
    Compare(name, source, target);
}

void Compare(string name, byte[] source, byte[] target)
{
    Console.WriteLine($"{name}: {source.Length:N0} source bytes, {target.Length:N0} target bytes");
    using var dictionary = new ByteBuffer(source);
    foreach (int width in defaults ? new[] { 16 } : new[] { 8, 16, 32 })
    {
#if UPSTREAM_API
        int minimum = 2 * width;
        VcEncoder CreateEncoder(Stream input, Stream output)
            => defaults ? new VcEncoder(dictionary, input, output)
                        : new VcEncoder(dictionary, input, output, maxBufferSize: 1, blockSize: width, chunkSize: minimum);
#else
        int minimum = width;
        VcEncoder CreateEncoder(Stream input, Stream output)
            => defaults ? new VcEncoder(dictionary, input, output, BlockSize.Bytes16)
                        : new VcEncoder(dictionary, input, output, maxBufferSize: 1, blockSize: (BlockSize)width,
                            chunkSize: minimum) { Matcher = candidate };
#endif
        ReadOnlyMemory<byte> Encode()
        {
            using var input = new MemoryStream(target, false);
            using var output = new MemoryStream();
            using var encoder = CreateEncoder(input, output);
            if (encoder.Encode() != VCDiffResult.SUCCESS)
                throw new InvalidOperationException("Encoding failed.");
            return output.GetBuffer().AsMemory(0, (int)output.Length);
        }

        var patch = Encode();
        if (benchmarkDecode)
        {
            CompareDecode(name, source, target, patch.ToArray(), width, minimum);
            continue;
        }

        Verify(source, target, patch);
        const int warmups = 5, samples = 21;
        int encodesPerSample = Math.Max(source.Length, target.Length) <= 64 * 1024 ? 32 : 1;
        var times = new double[samples];
        var allocations = new double[samples];
        for (int i = -warmups; i < samples; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int j = 0; j < encodesPerSample; j++)
                Encode();
            double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (i >= 0)
            {
                times[i] = elapsed / encodesPerSample;
                allocations[i] = (double)allocated / encodesPerSample;
            }
        }
        Array.Sort(times);
        Array.Sort(allocations);
        Console.WriteLine($"{name} | {width}/{minimum} | {times[samples / 2]:F6} ms | " +
            $"{patch.Length} patch bytes | {allocations[samples / 2]:F1} allocated B/op");
    }
}

static void Verify(byte[] source, byte[] target, ReadOnlyMemory<byte> patch)
{
    using var dictionary = new MemoryStream(source, false);
    using var input = new MemoryStream(patch.ToArray(), false);
    using var output = new MemoryStream();
    using var decoder = new VcDecoder(dictionary, input, output);
    if (decoder.Decode(out long written) != VCDiffResult.SUCCESS || written != target.Length ||
        !target.AsSpan().SequenceEqual(output.GetBuffer().AsSpan(0, (int)output.Length)))
        throw new InvalidDataException("Decoded output differs from the target.");
}

static void CompareDecode(string name, byte[] source, byte[] target, byte[] patch, int width, int minimum)
{
    using var sourceStream = new MemoryStream(source, writable: false);
    using var dictionary = new ByteBuffer(source);
    using var input = new MemoryStream(patch, writable: false);
    using var output = new MemoryStream(target.Length);

    foreach (bool useStream in new[] { true, false })
    {
        const int warmups = 5, samples = 21;
        var times = new double[samples];
        var allocations = new long[samples];
        for (int i = -warmups; i < samples; i++)
        {
            sourceStream.Position = dictionary.Position = input.Position = 0;
            output.SetLength(0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            VCDiffResult result;
            long written;
            if (useStream)
            {
                using var decoder = new VcDecoder(sourceStream, input, output);
                result = decoder.Decode(out written);
            }
            else
            {
                using var reader = new ByteStreamReader(input);
                using var decoder = new VcDecoderEx<ByteBuffer, ByteStreamReader>(dictionary, reader, output);
                result = decoder.Decode(out written);
            }
            double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (result != VCDiffResult.SUCCESS || written != target.Length ||
                !output.GetBuffer().AsSpan(0, (int)output.Length).SequenceEqual(target))
                throw new InvalidDataException("Decoded output differs from the target.");
            if (i >= 0)
            {
                times[i] = elapsed;
                allocations[i] = allocated;
            }
        }
        Array.Sort(times);
        Array.Sort(allocations);
        string kind = useStream ? "Stream" : "ByteBuffer";
        Console.WriteLine($"{name} | {width}/{minimum} | decode ({kind} dictionary) | " +
            $"{times[samples / 2]:F4} ms | {patch.Length} patch bytes | {allocations[samples / 2]} allocated B/op");
    }
}
