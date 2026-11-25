using System;
using System.IO;
using System.Threading.Tasks;
using VCDiff.Decoders;
using VCDiff.Encoders;
using VCDiff.Includes;
using VCDiff.Shared;
using Xunit;

namespace VCDiff.Tests
{
    public class DecoderSectionReadTests
    {
        private sealed class ShortReadPatch : MemoryStream
        {
            public ShortReadPatch(byte[] patch) : base(patch, writable: false) { }
            public override int Read(byte[] buffer, int offset, int count)
                => base.Read(buffer, offset, Math.Min(7, count));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task ReadsCompleteWindowSectionsFromAShortReadingPatch(bool interleaved, bool asynchronous)
        {
            var target = new byte[12345];
            new Random(19).NextBytes(target);
            using var source = new MemoryStream(new byte[1]);
            using var input = new MemoryStream(target);
            using var encoded = new MemoryStream();
            using var encoder = new VcEncoder(source, input, encoded, BlockSize.Bytes16);
            Assert.Equal(VCDiffResult.SUCCESS, encoder.Encode(interleaved, ChecksumFormat.SDCH));

            using var patch = new ShortReadPatch(encoded.ToArray());
            using var output = new MemoryStream();
            using var decoder = new VcDecoder(source, patch, output);
            VCDiffResult result;
            long written;
            if (asynchronous)
                (result, written) = await decoder.DecodeAsync();
            else
                result = decoder.Decode(out written);
            Assert.Equal(VCDiffResult.SUCCESS, result);
            Assert.Equal(target.LongLength, written);
            Assert.True(target.AsSpan().SequenceEqual(output.ToArray()));
        }
    }
}
