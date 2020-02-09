using System;
using System.IO;
using VCDiff.Decoders;
using VCDiff.Encoders;
using VCDiff.Includes;
using Xunit;

namespace VCDiff.Tests
{
    public class MattiIntegrationTests
    {

        [Fact]
        public void TestEncodeAndDecodeShouldBeTheSame()
        {
            int size = 20 * 1024 * 1024; // 20 MB

            byte[] oldData = CreateRandomByteArray(size);
            byte[] newData = new byte[size];

            oldData.CopyTo(newData, 0);

            AddRandomPiecesIn(oldData);

            var sOld = new MemoryStream(oldData);
            var sNew = new MemoryStream(newData);
            var sDelta = new MemoryStream(new byte[size], true);

            var coder = new VcEncoder(sOld, sNew, sDelta);
            Assert.Equal(VCDiffResult.SUCCESS, coder.Encode());

            sDelta.SetLength(sDelta.Position);
            sDelta.Position = 0;
            sOld.Position = 0;
            sNew.Position = 0;

            var sPatched = new MemoryStream(new byte[size], true);

            var decoder = new VcDecoder(sOld, sDelta, sPatched);
            Assert.Equal(VCDiffResult.SUCCESS, decoder.Decode(out long bytesWritten));


            Assert.Equal(sNew.ToArray(), sPatched.ToArray());
        }

        private static readonly Random random = new Random(DateTime.Now.GetHashCode());

        private byte[] CreateRandomByteArray(int size)
        {
            byte[] buffer = new byte[size];

            random.NextBytes(buffer);

            return buffer;
        }

        private void AddRandomPiecesIn(byte[] input)
        {
            int size = 1024 * 100; // 100 KB

            for (int i = 0; i < 100; i++)
            {
                byte[] difference = CreateRandomByteArray(size);

                int index = random.Next(0, input.Length - size - 1);

                for (int x = 0; x < size; x++)
                {
                    input[x + index] = difference[x];
                }
            }
        }

    }
}