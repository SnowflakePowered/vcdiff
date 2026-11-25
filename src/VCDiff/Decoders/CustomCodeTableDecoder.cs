using System;
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

        internal VCDiffResult Decode<T>(ref T source) where T : IByteBuffer
        {
            int length = VarIntBE.ParseInt32(source);
            if (length == (int)VCDiffResult.EOD || length > source.Length - source.Position)
                return VCDiffResult.EOD;
            if (length < 2)
                return VCDiffResult.ERROR;

            NearSize = source.ReadByte();
            SameSize = source.ReadByte();
            var encoded = new byte[length - 2];
            if (!BufferReads.TryReadExactly(ref source, encoded))
                return VCDiffResult.EOD;

            // RFC 3284 section 7 uses the default instruction table for the table delta itself.
            if (encoded.Length < 5 || (encoded[4] & (int)VCDiffCodeFlags.VCDCODETABLE) != 0)
                return VCDiffResult.ERROR;

            using var patch = new ByteBuffer(encoded);
            using var dictionary = CodeTable.DefaultTable.GetBytes();
            var tableBytes = new byte[CodeTable.SerializedSize];
            using var output = new MemoryStream(tableBytes, writable: true);
            using var decoder = new VcDecoderEx<ByteBuffer, ByteBuffer>(dictionary, patch, output,
                maxTargetFileSize: CodeTable.SerializedSize);
            try
            {
                if (decoder.Decode(out long written) != VCDiffResult.SUCCESS || written != tableBytes.Length)
                    return VCDiffResult.ERROR;
            }
            catch (NotSupportedException) when (output.Position > 0)
            {
                // Multiple windows may collectively exceed the fixed-size destination.
                return VCDiffResult.ERROR;
            }

            CustomTable = new CodeTable(tableBytes);
            return VCDiffResult.SUCCESS;
        }
    }
}
