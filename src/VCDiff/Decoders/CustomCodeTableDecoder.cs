using System.IO;
using VCDiff.Includes;
using VCDiff.Shared;

namespace VCDiff.Decoders
{
    internal class CustomCodeTableDecoder
    {
        public byte NearSize { get; private set; }

        public byte SameSize { get; private set; }

        public CodeTable? CustomTable { get; private set; }

        internal VCDiffResult Decode(IByteBuffer source)
        {
            //the custom codetable itself is a VCDiff file but it is required to be encoded with the standard table
            //the length should be the first thing after the hdr_indicator if not supporting compression
            //at least according to the RFC specs.
            int lengthOfCodeTable = VarIntBE.ParseInt32(source);

            if (lengthOfCodeTable == 0) return VCDiffResult.ERROR;

            using ByteBuffer codeTable = new ByteBuffer(source.ReadBytes(lengthOfCodeTable));

            //according to the RFC specifications the next two items will be the size of near and size of same
            //they are bytes in the RFC spec, but for some reason Google uses the varint to read which does
            //the same thing if it is a single byte
            //but I am going to just read in bytes because it is the RFC standard
            NearSize = codeTable.ReadByte();
            SameSize = codeTable.ReadByte();

            if (NearSize == 0 || SameSize == 0 || NearSize > byte.MaxValue || SameSize > byte.MaxValue)
            {
                return VCDiffResult.ERROR;
            }

            CustomTable = new CodeTable();
            //get the original bytes of the default codetable to use as a dictionary
            using ByteBuffer dictionary = CustomTable.GetBytes();

            //Decode the code table VCDiff file itself
            //stream the decoded output into a memory stream
            using MemoryStream sout = new MemoryStream();
            VcDecoder decoder = new VcDecoder(dictionary, codeTable, sout);
            var result = decoder.Decode(out long bytesWritten);

            if (result != VCDiffResult.SUCCESS || bytesWritten == 0)
            {
                return VCDiffResult.ERROR;
            }

            //set the new table data that was decoded
            if (!CustomTable.SetBytes(sout.ToArray()))
            {
                result = VCDiffResult.ERROR;
            }

            return result;
        }
    }
}