using System.IO;
using System.Text;
using System.Threading.Tasks;
using VCDiff.Decoders;
using VCDiff.Includes;
using Xunit;

namespace VCDiff.Tests
{
    public class CustomCodeTableTests
    {
        [Theory]
        [InlineData(false, false, true)]
        [InlineData(false, true, true)]
        [InlineData(true, false, true)]
        [InlineData(true, true, true)]
        [InlineData(false, false, false)]
        [InlineData(false, true, false)]
        public async Task RemappedCopyUsesDeclaredAddressCache(bool interleaved, bool useAsync, bool useSameCache)
        {
            // In the standard table opcode 4 means ADD(3). The inner delta copies
            // the default table except for its first type, size and mode at opcode 4.
            // These three ADDs make opcode 4 a COPY of four bytes in address mode 2.
            byte[] tableDelta =
            {
                0xD6, 0xC3, 0xC4, 0, 0,
                1, 0x8C, 0, 0, 28, 0x8C, 0, 0, 3, 13, 6,
                3, 4, 2,                                   // Replacement row entries
                20, 2, 19, 0x83, 0x7F, 2, 19, 0x83, 0x7F, 2, 19, 0x83, 0x7B,
                0, 5, 0x84, 5, 0x88, 5                    // COPY from 0, 5, 517 and 1029
            };
            using var patch = new MemoryStream();
            byte[] header = { 0xD6, 0xC3, 0xC4, interleaved ? (byte)'S' : (byte)0, 2,
                (byte)(tableDelta.Length + 2), useSameCache ? (byte)0 : (byte)7,
                useSameCache ? (byte)7 : (byte)0 };
            patch.Write(header, 0, header.Length);
            patch.Write(tableDelta, 0, tableDelta.Length);

            // The first COPY uses SELF(130) and updates the caches. In mode 2,
            // SAME(130) selects that slot, while NEAR(0) adds zero to its address.
            byte operand = useSameCache ? (byte)130 : (byte)0;
            byte[] window = interleaved
                ? new byte[] { 1, 0x81, 6, 0, 10, 8, 0, 0, 5, 0, 20, 0x81, 2, 4, operand }
                : new byte[] { 1, 0x81, 6, 0, 10, 8, 0, 0, 2, 3, 20, 4, 0x81, 2, operand };
            patch.Write(window, 0, window.Length);
            patch.Position = 0;
            var bytes = new byte[134];
            Encoding.ASCII.GetBytes("cat!").CopyTo(bytes, 130);
            using var source = new MemoryStream(bytes);
            using var target = new MemoryStream();
            using var decoder = new VcDecoder(source, patch, target);

            VCDiffResult status;
            long written;
            if (useAsync) (status, written) = await decoder.DecodeAsync();
            else status = decoder.Decode(out written);
            Assert.Equal(VCDiffResult.SUCCESS, status);
            Assert.Equal(8L, written);
            Assert.Equal("cat!cat!", Encoding.ASCII.GetString(target.ToArray()));
        }
    }
}
