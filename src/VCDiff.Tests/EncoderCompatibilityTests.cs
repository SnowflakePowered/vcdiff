using System;
using System.IO;
using VCDiff.Encoders;
using VCDiff.Includes;
using VCDiff.Shared;
using Xunit;

namespace VCDiff.Tests
{
    public class EncoderCompatibilityTests
    {
        [Theory]
        [InlineData(int.MinValue, 0, BlockSize.Bytes8, 16)]
        [InlineData(6, 4, BlockSize.Bytes8, 8)]
        [InlineData(12, 0, BlockSize.Bytes8, 16)]
        [InlineData(13, 0, BlockSize.Bytes16, 32)]
        [InlineData(16, 33, BlockSize.Bytes16, 33)]
        [InlineData(24, 0, BlockSize.Bytes16, 32)]
        [InlineData(25, 16, BlockSize.Bytes32, 32)]
        [InlineData(48, 96, BlockSize.Bytes32, 96)]
        [InlineData(int.MaxValue, 0, BlockSize.Bytes32, 64)]
        public void IntegerOptionsMatchRoundedEnumOptions(int requested, int minimum, BlockSize expected, int expectedMinimum)
        {
            byte[] source = new byte[16384];
            new Random(43).NextBytes(source);
            using var fragments = new MemoryStream();
            for (int length = 8; length <= 100; length++)
            {
                fragments.WriteByte(0xff);
                fragments.Write(source, length * 129, length);
            }
            byte[] target = fragments.ToArray();
            using var dictionary = new ByteBuffer(source);
            using var stream = new MemoryStream(source);
            using var input = new MemoryStream(target);
            using var reference = new MemoryStream();
            using var correct = new VcEncoder(dictionary, input, reference, expected, expectedMinimum);
            Assert.Equal(VCDiffResult.SUCCESS, correct.Encode());

            using var unused = new RollingHash(1);
            using var fromBuffer = new MemoryStream();
            using var fromStream = new MemoryStream();
            using var encoder1 = new VcEncoder(dictionary, input, fromBuffer, 1, requested, minimum, unused);
            using var encoder2 = new VcEncoder(stream, input, fromStream, 1, requested, minimum, null);
            Assert.Equal(VCDiffResult.SUCCESS, encoder1.Encode());
            Assert.Equal(VCDiffResult.SUCCESS, encoder2.Encode());
            Assert.Equal(reference.ToArray(), fromBuffer.ToArray());
            Assert.Equal(reference.ToArray(), fromStream.ToArray());
        }

        [Fact]
        public void ThreeArgumentsUseUpstreamCopyMinimum()
        {
            byte[] source = new byte[64];
            new Random(3).NextBytes(source);
            using var dictionary = new ByteBuffer(source);
            using var target = new MemoryStream(source.AsSpan(0, 16).ToArray());
            using var legacy = new MemoryStream();
            using var explicitEnum = new MemoryStream();
            using var expected = new MemoryStream();
            using var first = new VcEncoder(dictionary, target, legacy);
            using var second = new VcEncoder(dictionary, target, explicitEnum, BlockSize.Bytes16);
            using var third = new VcEncoder(dictionary, target, expected, BlockSize.Bytes16, 32);
            Assert.Equal(VCDiffResult.SUCCESS, first.Encode());
            Assert.Equal(VCDiffResult.SUCCESS, second.Encode());
            Assert.Equal(VCDiffResult.SUCCESS, third.Encode());
            Assert.Equal(expected.ToArray(), legacy.ToArray());
            Assert.True(explicitEnum.Length < legacy.Length);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(16)]
        [InlineData(48)]
        public void StandaloneRollingHashStillAdvancesTheWindow(int size)
        {
            byte[] data = new byte[size + 64];
            new Random(29).NextBytes(data);
            ulong Fresh(int offset)
            {
                ulong h = 0;
                for (int i = offset; i < offset + size; i++)
                    h = (257 * h + data[i]) & 0x7fffff;
                return h;
            }
            using var rolling = new RollingHash(size);
            Assert.Equal(size, rolling.WindowSize);
            ulong hash = Fresh(0);
            for (int i = 0; i < data.Length - size; i++)
            {
                hash = rolling.UpdateHash(hash, data[i], data[i + size]);
                Assert.Equal(Fresh(i + 1), hash);
            }
        }
    }
}
