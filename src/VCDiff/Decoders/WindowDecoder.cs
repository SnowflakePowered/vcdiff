using System;
using VCDiff.Includes;
using VCDiff.Shared;

namespace VCDiff.Decoders
{
    internal class WindowDecoder
    {
        private IByteBuffer buffer;
        private int returnCode;
        private long deltaEncodingLength;
        private long deltaEncodingStart;
        private ParseableChunk chunk;
        private byte deltaIndicator;
        private readonly long dictionarySize;
        private byte winIndicator;
        private long sourceSegmentLength;
        private long sourceSegmentOffset;
        private long targetLength;
        private long addRunLength;
        private long instructionAndSizesLength;
        private long addressForCopyLength;
        private uint checksum;

        public Memory<byte> AddRunData { get; private set; }

        public Memory<byte> InstructionsAndSizesData { get; private set; }

        public Memory<byte> AddressesForCopyData { get; private set; }

        public long AddRunLength => addRunLength;

        public long InstructionAndSizesLength => instructionAndSizesLength;

        public long AddressesForCopyLength => addressForCopyLength;

        public byte WinIndicator => winIndicator;

        public long SourceSegmentOffset => sourceSegmentOffset;

        public long SourceSegmentLength => sourceSegmentLength;

        public long TargetWindowLength => targetLength;

        public uint Checksum => checksum;

        public ChecksumFormat ChecksumFormat { get; private set; }

        public int Result => returnCode;

        /// <summary>
        /// Parses the window from the data
        /// </summary>
        /// <param name="dictionarySize">the dictionary size</param>
        /// <param name="buffer">the buffer containing the incoming data</param>
        public WindowDecoder(long dictionarySize, IByteBuffer buffer)
        {
            this.dictionarySize = dictionarySize;
            this.buffer = buffer;
            chunk = new ParseableChunk(buffer.Position, buffer.Length);
            returnCode = (int)VCDiffResult.SUCCESS;
        }

        /// <summary>
        /// Decodes the window header.
        /// </summary>
        /// <param name="isSdch">If the delta uses SDCH extensions.</param>
        /// <returns></returns>
        public bool Decode(bool isSdch)
        {
            if (!ParseWindowIndicatorAndSegment(dictionarySize, 0, false, out winIndicator, out sourceSegmentLength, out sourceSegmentOffset))
            {
                return false;
            }

            if (!ParseWindowLengths(out targetLength))
            {
                return false;
            }

            if (!ParseDeltaIndicator())
            {
                return false;
            }

            this.ChecksumFormat = ChecksumFormat.None;
            if ((winIndicator & (int)VCDiffWindowFlags.VCDCHECKSUM) != 0)
            {
                this.ChecksumFormat = isSdch ? ChecksumFormat.SDCH : ChecksumFormat.Xdelta3;
            }

            if (!ParseSectionLengths(this.ChecksumFormat, out addRunLength, out instructionAndSizesLength, out addressForCopyLength, out checksum))
            {
                return false;
            }

            if (isSdch && addRunLength == 0 && addressForCopyLength == 0 && instructionAndSizesLength > 0)
            {
                //interleave format
                return true;
            }

            if (buffer.CanRead)
            {
                AddRunData = buffer.ReadBytes((int)addRunLength);
            }
            if (buffer.CanRead)
            {
                InstructionsAndSizesData = buffer.ReadBytes((int)instructionAndSizesLength);
            }
            if (buffer.CanRead)
            {
                AddressesForCopyData = buffer.ReadBytes((int)addressForCopyLength);
            }

            return true;
        }

        private bool ParseByte(out byte value)
        {
            if ((int)VCDiffResult.SUCCESS != returnCode)
            {
                value = 0;
                return false;
            }
            if (chunk.IsEmpty)
            {
                value = 0;
                returnCode = (int)VCDiffResult.EOD;
                return false;
            }
            value = buffer.ReadByte();
            chunk.Position = buffer.Position;
            return true;
        }

        private bool ParseInt32(out int value)
        {
            if ((int)VCDiffResult.SUCCESS != returnCode)
            {
                value = 0;
                return false;
            }
            if (chunk.IsEmpty)
            {
                value = 0;
                returnCode = (int)VCDiffResult.EOD;
                return false;
            }

            int parsed = VarIntBE.ParseInt32(buffer);
            switch (parsed)
            {
                case (int)VCDiffResult.ERROR:
                    value = 0;
                    return false;

                case (int)VCDiffResult.EOD:
                    value = 0;
                    return false;
            }
            chunk.Position = buffer.Position;
            value = parsed;
            return true;
        }

        private bool ParseUInt32(out uint value)
        {
            if ((int)VCDiffResult.SUCCESS != returnCode)
            {
                value = 0;
                return false;
            }
            if (chunk.IsEmpty)
            {
                value = 0;
                returnCode = (int)VCDiffResult.EOD;
                return false;
            }

            long parsed = VarIntBE.ParseInt64(buffer);
            switch (parsed)
            {
                case (int)VCDiffResult.ERROR:
                    value = 0;
                    return false;

                case (int)VCDiffResult.EOD:
                    value = 0;
                    return false;
            }
            if (parsed > 0xFFFFFFFF)
            {
                returnCode = (int)VCDiffResult.ERROR;
                value = 0;
                return false;
            }
            chunk.Position = buffer.Position;
            value = (uint)parsed;
            return true;
        }

        private bool ParseSourceSegmentLengthAndPosition(long from, out long sourceLength, out long sourcePosition)
        {
            if (!ParseInt32(out int outLength))
            {
                sourceLength = 0;
                sourcePosition = 0;
                return false;
            }
            sourceLength = outLength;
            if (sourceLength > from)
            {
                returnCode = (int)VCDiffResult.ERROR;
                sourceLength = 0;
                sourcePosition = 0;
                return false;
            }
            if (!ParseInt32(out int outPos))
            {
                sourcePosition = 0;
                sourceLength = 0;
                return false;
            }
            sourcePosition = outPos;
            if (sourcePosition > from)
            {
                returnCode = (int)VCDiffResult.ERROR;
                sourceLength = 0;
                sourcePosition = 0;
                return false;
            }

            long segmentEnd = sourcePosition + sourceLength;
            if (segmentEnd > from)
            {
                returnCode = (int)VCDiffResult.ERROR;
                sourceLength = 0;
                sourcePosition = 0;
                return false;
            }

            return true;
        }

        private bool ParseWindowIndicatorAndSegment(long dictionarySize, long decodedTargetSize, bool allowVCDTarget, out byte winIndicator, out long sourceSegmentLength, out long sourceSegmentPosition)
        {
            if (!ParseByte(out winIndicator))
            {
                winIndicator = 0;
                sourceSegmentLength = 0;
                sourceSegmentPosition = 0;
                return false;
            }

            int sourceFlags = winIndicator & ((int)VCDiffWindowFlags.VCDSOURCE | (int)VCDiffWindowFlags.VCDTARGET);

            switch (sourceFlags)
            {
                case (int)VCDiffWindowFlags.VCDSOURCE:
                    return ParseSourceSegmentLengthAndPosition(dictionarySize, out sourceSegmentLength, out sourceSegmentPosition);

                case (int)VCDiffWindowFlags.VCDTARGET:
                    if (!allowVCDTarget)
                    {
                        winIndicator = 0;
                        sourceSegmentLength = 0;
                        sourceSegmentPosition = 0;
                        returnCode = (int)VCDiffResult.ERROR;
                        return false;
                    }
                    return ParseSourceSegmentLengthAndPosition(decodedTargetSize, out sourceSegmentLength, out sourceSegmentPosition);

                case (int)VCDiffWindowFlags.VCDSOURCE | (int)VCDiffWindowFlags.VCDTARGET:
                    winIndicator = 0;
                    sourceSegmentPosition = 0;
                    sourceSegmentLength = 0;
                    return false;
            }

            winIndicator = 0;
            sourceSegmentPosition = 0;
            sourceSegmentLength = 0;
            return false;
        }

        private bool ParseWindowLengths(out long targetWindowLength)
        {
            if (!ParseInt32(out int deltaLength))
            {
                targetWindowLength = 0;
                return false;
            }
            deltaEncodingLength = deltaLength;

            deltaEncodingStart = chunk.ParsedSize;
            if (!ParseInt32(out int outTargetLength))
            {
                targetWindowLength = 0;
                return false;
            }
            targetWindowLength = outTargetLength;
            return true;
        }

        private bool ParseDeltaIndicator()
        {
            if (!ParseByte(out deltaIndicator))
            {
                returnCode = (int)VCDiffResult.ERROR;
                return false;
            }
            if ((deltaIndicator & ((int)VCDiffCompressFlags.VCDDATACOMP | (int)VCDiffCompressFlags.VCDINSTCOMP | (int)VCDiffCompressFlags.VCDADDRCOMP)) > 0)
            {
                returnCode = (int)VCDiffResult.ERROR;
                return false;
            }
            return true;
        }

        public bool ParseSectionLengths(ChecksumFormat checksumFormat, out long addRunLength, out long instructionsLength, out long addressLength, out uint checksum)
        {
            ParseInt32(out int outAdd);
            ParseInt32(out int outInstruct);
            ParseInt32(out int outAddress);
            checksum = 0;

            if (checksumFormat == ChecksumFormat.SDCH)
            {
                ParseUInt32(out checksum);
            } 
            else if (checksumFormat == ChecksumFormat.Xdelta3)
            {
                // xdelta checksum is stored as a 4-part byte array
                ParseByte(out byte chk0);
                ParseByte(out byte chk1);
                ParseByte(out byte chk2);
                ParseByte(out byte chk3);
                checksum = (uint)(chk0 << 24 | chk1 << 16 | chk2 << 8 | chk3);
            }

            addRunLength = outAdd;
            addressLength = outAddress;
            instructionsLength = outInstruct;

            if (returnCode != (int)VCDiffResult.SUCCESS)
            {
                return false;
            }

            long deltaHeaderLength = chunk.ParsedSize - deltaEncodingStart;
            long totalLen = deltaHeaderLength + addRunLength + instructionsLength + addressLength;

            if (deltaEncodingLength == totalLen) return true;
            returnCode = (int)VCDiffResult.ERROR;
            return false;

        }

        public class ParseableChunk
        {
            private long end;
            private long position;
            private long start;

            public long UnparsedSize => end - position;

            public long End => end;

            public bool IsEmpty => 0 == UnparsedSize;

            public long Start => start;

            public long ParsedSize => position - start;

            public long Position
            {
                get => position;
                set
                {
                    if (position < start)
                    {
                        return;
                    }
                    if (position > end)
                    {
                        return;
                    }
                    position = value;
                }
            }

            public ParseableChunk(long s, long len)
            {
                start = s;
                end = s + len;
                position = s;
            }
        }
    }
}