using System.Buffers.Binary;
using System.IO;
using VCDiff.Encoders;
using VCDiff.Includes;
using VCDiff.Shared;
using Xunit;

namespace VCDiff.Tests
{
    public class WindowChecksumTests
    {
        // Independent zlib.adler32 vectors for the byte sequence below, seeded with
        // 1 for xdelta3 and 0 for SDCH. 5,552 is the Adler-32 reduction interval.
        [Theory]
        [InlineData(1,    0x00080008u, 0x00070007u)]
        [InlineData(15,   0x2DC7064Fu, 0x2DB8064Eu)]
        [InlineData(16,   0x34D00709u, 0x34C00708u)]
        [InlineData(5552, 0xB9EECD6Fu, 0xA43ECD6Eu)]
        [InlineData(5553, 0x8863CE66u, 0x72B2CE65u)]
        public void WindowContainsChecksumOfCompleteTarget(int length, uint xdelta3, uint sdch)
        {
            byte[] target = new byte[length];
            for (int i = 0; i < target.Length; i++)
                target[i] = (byte)((i * 29 + 7) & 255);

            Assert.Equal(xdelta3, EncodeAndReadChecksum(target, ChecksumFormat.Xdelta3));
            Assert.Equal(sdch, EncodeAndReadChecksum(target, ChecksumFormat.SDCH));
        }

        private static uint EncodeAndReadChecksum(byte[] target, ChecksumFormat format)
        {
            using var dictionary = new ByteBuffer(new byte[1]);
            using var input = new MemoryStream(target);
            using var patch = new MemoryStream();
            using var encoder = new VcEncoder(dictionary, input, patch, BlockSize.Bytes16);
            Assert.Equal(VCDiffResult.SUCCESS, encoder.Encode(checksumFormat: format));

            patch.Position = 5; // First window after the standard or SDCH file header.
            Assert.Equal(5, patch.ReadByte()); // VCD_SOURCE | VCD_CHECKSUM
            Assert.Equal(1u, ReadInteger(patch)); // Source length
            Assert.Equal(0u, ReadInteger(patch)); // Source offset
            ReadInteger(patch); // Encoded window length
            Assert.Equal((uint)target.Length, ReadInteger(patch));
            Assert.Equal(0, patch.ReadByte()); // Uncompressed sections
            ReadInteger(patch); // Literal data length
            ReadInteger(patch); // Instruction length
            ReadInteger(patch); // Address length

            if (format == ChecksumFormat.SDCH)
                return ReadInteger(patch);
            byte[] bytes = new byte[4];
            Assert.Equal(bytes.Length, patch.Read(bytes, 0, bytes.Length));
            return BinaryPrimitives.ReadUInt32BigEndian(bytes);
        }

        private static uint ReadInteger(Stream input)
        {
            uint value = 0;
            int b;
            do
            {
                b = input.ReadByte();
                Assert.NotEqual(-1, b);
                value = checked(value * 128 + (uint)(b & 0x7F));
            } while ((b & 0x80) != 0);
            return value;
        }
    }
}
