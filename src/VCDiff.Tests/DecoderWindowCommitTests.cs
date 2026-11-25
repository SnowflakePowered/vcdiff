using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VCDiff.Decoders;
using VCDiff.Includes;
using VCDiff.Shared;
using Xunit;

namespace VCDiff.Tests
{
    public class DecoderWindowCommitTests
    {
        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, false, true)]
        [InlineData(false, true, false)]
        [InlineData(false, true, true)]
        [InlineData(true, true, false)]
        [InlineData(true, true, true)]
        public async Task OnlyVerifiedWindowsReachCallerOutput(bool interleaved, bool sdch, bool useAsync)
        {
            // All three windows carry the checksum of 'A'. Only the second contains 'B'.
            byte[] patch = Header(sdch).Concat(Window(interleaved, sdch, (byte)'A'))
                .Concat(Window(interleaved, sdch, (byte)'B')).Concat(Window(interleaved, sdch, (byte)'A')).ToArray();
            foreach (bool bypass in new[] { false, true })
            {
                using var dictionary = new MemoryStream();
                using var delta = new MemoryStream(patch);
                using var output = new MemoryStream();
                output.WriteByte(77);
                using var decoder = new VcDecoder(dictionary, delta, output, disableChecksums: bypass);

                var result = await Decode(decoder, useAsync);
                Assert.Equal(bypass ? VCDiffResult.SUCCESS : VCDiffResult.ERROR, result.status);
                Assert.Equal(bypass ? 3L : 1L, result.written);
                Assert.Equal(bypass ? new byte[] { 77, 65, 66, 65 } : new byte[] { 77, 65 }, output.ToArray());
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task ResumeAfterPartialHeaderAndPartialSecondWindow(bool interleaved, bool useAsync)
        {
            byte[] header = Header(interleaved);
            byte[] window = Window(interleaved, interleaved, (byte)'A');
            byte[] patch = header.Concat(window).Concat(window).ToArray();
            using var dictionary = new MemoryStream();
            using var delta = new MemoryStream();
            using var output = new MemoryStream();
            using var decoder = new VcDecoder(dictionary, delta, output);

            void SupplyThrough(int end)
            {
                long resume = delta.Position;
                int start = (int)delta.Length;
                delta.Position = start;
                delta.Write(patch, start, end - start);
                delta.Position = resume;
            }

            SupplyThrough(3);
            Assert.Equal((VCDiffResult.EOD, 0L), await Decode(decoder, useAsync));
            Assert.Equal(0L, delta.Position);

            SupplyThrough(patch.Length - 1);
            Assert.Equal((VCDiffResult.EOD, 1L), await Decode(decoder, useAsync));
            Assert.Equal((long)header.Length + window.Length, delta.Position);
            Assert.Equal(new byte[] { 65 }, output.ToArray());

            SupplyThrough(patch.Length);
            Assert.Equal((VCDiffResult.SUCCESS, 1L), await Decode(decoder, useAsync));
            Assert.Equal(new byte[] { 65, 65 }, output.ToArray());
            Assert.Equal((VCDiffResult.EOD, 0L), await Decode(decoder, useAsync));
        }

        private static byte[] Header(bool sdch) => new byte[] { 0xD6, 0xC3, 0xC4, sdch ? (byte)'S' : (byte)0, 0 };

        private static byte[] Window(bool interleaved, bool sdch, byte literal)
        {
            // One target byte, no dictionary, ADD(1). Checksum is Adler-32 for 'A':
            // 0x00410041 for SDCH, 0x00420042 for xdelta3.
            return new byte[] { 4, 11, 1, 0, (byte)(interleaved ? 0 : 1), (byte)(interleaved ? 2 : 1), 0 }
                .Concat(sdch ? new byte[] { 0x82, 0x84, 0x80, 0x41 } : new byte[] { 0, 0x42, 0, 0x42 })
                .Concat(interleaved ? new byte[] { 2, literal } : new byte[] { literal, 2 }).ToArray();
        }

        private static async Task<(VCDiffResult status, long written)> Decode(VcDecoder decoder, bool useAsync)
        {
            if (useAsync) return await decoder.DecodeAsync();
            var status = decoder.Decode(out long written);
            return (status, written);
        }
    }
}
