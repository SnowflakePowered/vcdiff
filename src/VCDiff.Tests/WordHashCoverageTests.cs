using System;
using System.IO;
using System.Linq;
using VCDiff.Decoders;
using VCDiff.Encoders;
using VCDiff.Includes;
using VCDiff.Shared;
using Xunit;

namespace VCDiff.Tests
{
    public class WordHashCoverageTests
    {
        [Theory]
        [InlineData(9, 9)]
        [InlineData(10, 0)]
        public void PartialSeedInLastShortProbeUsesExactCombinedMatchLength(int minimum, int expectedCopy)
        {
            byte[] source = Enumerable.Range(0, 24).Select(i => (byte)i).ToArray();
            byte[] target = { 2, 3, 4, 5, 6, 7, 8, 9, 10, 0xF5, 0xF6, 0xF7, 0xFB, 0, 0xFA };
            // The seed at target[6] shares bucket 181 with source[8]. Only three
            // bytes match forwards; the six matching bytes to the left make nine.
            AssertCopyRange(source, target, BlockSize.Bytes8, minimum, 0, expectedCopy);
        }

        [Theory]
        [InlineData(8, 7, 31)]
        [InlineData(16, 15, 47)]
        [InlineData(32, 16, 65)]
        [InlineData(32, 17, 79)]
        [InlineData(32, 31, 81)]
        public void UnalignedMatchStopsAtLiteralBytesOnBothSides(int width, int left, int length)
        {
            byte[] source = Enumerable.Range(0, 192).Select(i => (byte)i).ToArray();
            // The first aligned source seed is at offset 64. Vary its distance to
            // the start of the match across word and vector boundaries.
            byte[] target = new byte[1 + length + 1];
            target[0] = 0xFE;
            Array.Copy(source, 64 - left, target, 1, length);
            target[target.Length - 1] = 0xFF;

            AssertCopyRange(source, target, (BlockSize)width, length, 1, length);
        }

        private static void AssertCopyRange(byte[] source, byte[] target, BlockSize width,
            int minimum, int copyStart, int copyLength)
        {
            using var dictionary = new ByteBuffer(source);
            using var input = new MemoryStream(target);
            using var patch = new MemoryStream();
            using var encoder = new VcEncoder(dictionary, input, patch, width, minimum);
            Assert.Equal(VCDiffResult.SUCCESS, encoder.Encode());
            Assert.Equal(target, Apply(source, patch));

            // The unchecksummed patch reads changed dictionary bytes only at COPY
            // positions. This checks the exact COPY extent without depending on a
            // particular VCDIFF address mode or instruction encoding.
            const byte marker = 0xCC;
            byte[] markedSource = Enumerable.Repeat(marker, source.Length).ToArray();
            byte[] expected = (byte[])target.Clone();
            expected.AsSpan(copyStart, copyLength).Fill(marker);
            Assert.Equal(expected, Apply(markedSource, patch));
        }

        private static byte[] Apply(byte[] source, MemoryStream patch)
        {
            patch.Position = 0;
            using var dictionary = new MemoryStream(source);
            using var output = new MemoryStream();
            using var decoder = new VcDecoder(dictionary, patch, output);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long written));
            Assert.Equal(output.Length, written);
            return output.ToArray();
        }
    }
}
