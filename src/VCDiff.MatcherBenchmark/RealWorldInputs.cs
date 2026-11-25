using System.IO.Compression;
using System.Security.Cryptography;

internal static class RealWorldInputs
{
    private const string Books = "https://raw.githubusercontent.com/israellot/DeltaSharp/677ebb03d71bebe9dd71ac4e9575c137277c2988/src/Benchmarks/Data";
    private const string SQLite = "https://www.sqlite.org/2024/sqlite-tools-win-x64-";

    internal static async Task<(byte[] Source, byte[] Target)> MobyDick(HttpClient client)
    {
        byte[] source = await Download(client, $"{Books}/pg15.txt",
            "7da8e626fa38865c93f4d356e51b9eaa3434ace8ceb95e3a9ab8dc609c8670bd");
        byte[] target = await Download(client, $"{Books}/pg2701.txt",
            "f29bf5abe1524aaa1d6e81c3a9e6bad9dca2433fa13d550b013e94589d7d65ac");
        return (source, target);
    }

    internal static async Task<(byte[] Source, byte[] Target)> SQLiteUpdate(HttpClient client)
    {
        byte[] source = await Download(client, $"{SQLite}3460000.zip",
            "e9248cbfbcf23ca7faabd0c3eadb9119b047296784c45d47f6c6f5bc306366bb", "sqlite3.exe");
        byte[] target = await Download(client, $"{SQLite}3460100.zip",
            "0e56e76eed7efaaf74884cf7c5337ebc65bf1c22eef2f513f4c4b7c8e8dfbb07", "sqlite3.exe");
        return (source, target);
    }

    private static async Task<byte[]> Download(HttpClient client, string url, string sha256, string member = null)
    {
        byte[] bytes = await client.GetByteArrayAsync(url);
        if (member != null)
        {
            using var archive = new ZipArchive(new MemoryStream(bytes, writable: false));
            var entry = archive.GetEntry(member) ?? throw new InvalidDataException($"Missing {member} in {url}.");
            using var input = entry.Open();
            using var output = new MemoryStream();
            input.CopyTo(output);
            bytes = output.ToArray();
        }

        if (!SHA256.HashData(bytes).AsSpan().SequenceEqual(Convert.FromHexString(sha256)))
            throw new InvalidDataException($"SHA-256 mismatch for {url} {member}.");
        return bytes;
    }
}
