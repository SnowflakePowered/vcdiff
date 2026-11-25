using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using VCDiff.Decoders;
using VCDiff.Includes;
using VCDiff.Shared;
using Xunit;

namespace VCDiff.Tests
{
    public class DecoderBehaviorTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task CopiesFromDictionaryAndCurrentWindow(bool interleaved, bool useAsync)
        {
            // RFC 3284 section 3 example, followed by a nonoverlapping COPY of target[0..4].
            // COPY(4,0), ADD("wxyz"), COPY(4,4), COPY(12,24), RUN(4,'z'), COPY(4,16).
            byte[] patch = interleaved
                ? new byte[]
                {
                    0xD6, 0xC3, 0xC4, (byte)'S', 0,
                    1, 16, 0, 21, 32, 0, 0, 16, 0,
                    20, 0, 5, (byte)'w', (byte)'x', (byte)'y', (byte)'z',
                    20, 4, 28, 24, 0, 4, (byte)'z', 20, 16
                }
                : new byte[]
                {
                    0xD6, 0xC3, 0xC4, 0, 0,
                    1, 16, 0, 21, 32, 0, 5, 7, 4,
                    (byte)'w', (byte)'x', (byte)'y', (byte)'z', (byte)'z',
                    20, 5, 20, 28, 0, 4, 20,
                    0, 4, 24, 16
                };

            var result = await Apply("abcdefghijklmnop", patch, useAsync);

            Assert.Equal(VCDiffResult.SUCCESS, result.Status);
            Assert.Equal(32L, result.Written);
            Assert.Equal("abcdwxyzefghefghefghefghzzzzabcd", result.Text);
            Assert.Equal(interleaved, result.IsSdch);
        }

        [Fact]
        public async Task ReadsExplicitDefaultCodeTableBeforeFirstWindow()
        {
            // The inner delta describes an identity code table by copying all 1,536 bytes
            // from the standard table. The outer window contains one ADD with opcode 4.
            byte[] patch =
            {
                0xD6, 0xC3, 0xC4, 0, 2, 22, 4, 3,
                0xD6, 0xC3, 0xC4, 0, 0,
                1, 0x8C, 0, 0, 10, 0x8C, 0, 0, 0, 3, 1, 19, 0x8C, 0, 0,
                0, 9, 3, 0, 3, 1, 0, (byte)'c', (byte)'a', (byte)'t', 4
            };

            var result = await Apply("", patch, false);
            Assert.Equal(VCDiffResult.SUCCESS, result.Status);
            Assert.Equal(3L, result.Written);
            Assert.Equal("cat", result.Text);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MissingHeaderBytesReturnEndOfData(bool useAsync)
        {
            byte[] header = { 0xD6, 0xC3, 0xC4, 0, 0 };
            for (int count = 0; count < header.Length; count++)
            {
                var result = await Apply("", header.AsSpan(0, count).ToArray(), useAsync);
                Assert.Equal(VCDiffResult.EOD, result.Status);
                Assert.Equal(0L, result.Written);
                Assert.Empty(result.Text);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ChecksumControlsWhetherAlternateDictionaryIsAccepted(bool useAsync)
        {
            // A source COPY of four bytes with xdelta3's Adler-32 for "abcd".
            byte[] patch =
            {
                0xD6, 0xC3, 0xC4, 0, 0,
                5, 4, 0, 11, 4, 0, 0, 1, 1, 0x03, 0xD8, 0x01, 0x8B, 20, 0
            };

            var original = await Apply("abcd", patch, useAsync);
            Assert.Equal(VCDiffResult.SUCCESS, original.Status);
            Assert.Equal("abcd", original.Text);

            var rejected = await Apply("wxyz", patch, useAsync);
            Assert.Equal(VCDiffResult.ERROR, rejected.Status);

            var accepted = await Apply("wxyz", patch, useAsync, disableChecksums: true);
            Assert.Equal(VCDiffResult.SUCCESS, accepted.Status);
            Assert.Equal(4L, accepted.Written);
            Assert.Equal("wxyz", accepted.Text);
        }

        private static async Task<(VCDiffResult Status, long Written, string Text, bool IsSdch)> Apply(
            string source, byte[] patch, bool useAsync, bool disableChecksums = false)
        {
            using var dictionary = new ByteBuffer(Encoding.ASCII.GetBytes(source));
            using var delta = new ByteBuffer(patch);
            using var output = new MemoryStream();
            using var decoder = new VcDecoderEx<ByteBuffer, ByteBuffer>(
                dictionary, delta, output, disableChecksums: disableChecksums);

            VCDiffResult status;
            long written;
            if (useAsync)
                (status, written) = await decoder.DecodeAsync();
            else
                status = decoder.Decode(out written);
            return (status, written, Encoding.ASCII.GetString(output.ToArray()), decoder.IsSDCHFormat);
        }
    }
}
