using System;
using System.IO;
using VCDiff.Shared;

namespace VCDiff.Encoders
{
    internal interface IWindowMatcher : IDisposable
    {
        void Encode(ReadOnlySpan<byte> target, WindowEncoder window, int minimumCopyLength);
    }

    internal sealed class ChunkEncoder : IDisposable
    {
        private readonly IWindowMatcher? matcher;
        private readonly long dictionarySize;
        private readonly int minimumCopyLength;
        private readonly ChecksumFormat checksumFormat;
        private readonly bool interleaved;

        internal ChunkEncoder(IWindowMatcher? matcher, long dictionarySize, int minimumCopyLength,
            ChecksumFormat checksumFormat, bool interleaved)
        {
            this.matcher = matcher;
            this.dictionarySize = dictionarySize;
            this.minimumCopyLength = minimumCopyLength;
            this.checksumFormat = checksumFormat;
            this.interleaved = interleaved;
        }

        public void EncodeChunk(ReadOnlySpan<byte> target, Stream outputStream)
        {
            uint checksum = checksumFormat switch
            {
                ChecksumFormat.SDCH => Checksum.ComputeGoogleAdler32(target),
                ChecksumFormat.Xdelta3 => Checksum.ComputeXdelta3Adler32(target),
                _ => 0
            };
            using var window = new WindowEncoder(dictionarySize, checksum, checksumFormat, interleaved);
            if (matcher == null)
                window.Add(target);
            else
                matcher.Encode(target, window, minimumCopyLength);
            window.Output(outputStream);
        }

        public void Dispose() => matcher?.Dispose();
    }
}
