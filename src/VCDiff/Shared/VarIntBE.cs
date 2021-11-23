﻿using System;
using System.Collections.Generic;
using System.IO;
using VCDiff.Includes;

namespace VCDiff.Shared
{
    internal class VarIntBE
    {
        /// <summary>
        /// Special VarIntBE class for encoding a Variable BE Integer
        /// </summary>
        public const int int32Max = 5;

        public const int int64Max = 9;

        public const int int32MaxValue = 0x7FFFFFFF;
        public const long int64MaxValue = 0x7FFFFFFFFFFFFFFF;

        public static int ParseInt32<TByteBuffer>(TByteBuffer sin) where TByteBuffer : IByteBuffer
        {
            int result = 0;
            while (sin.CanRead)
            {
                result += sin.PeekByte() & 0x7f;
                if ((sin.PeekByte() & 0x80) == 0)
                {
                    sin.Next();
                    return result;
                }
                if (result > (int32MaxValue >> 7))
                {
                    return (int)VCDiffResult.ERROR;
                }
                result = result << 7;
                sin.Next();
            }
            return (int)VCDiffResult.EOD;
        }

        public static long ParseInt64<TByteBuffer>(TByteBuffer sin) where TByteBuffer : IByteBuffer
        {
            long result = 0;
            while (sin.CanRead)
            {
                result += sin.PeekByte() & 0x7F;
                if ((sin.PeekByte() & 0x80) == 0)
                {
                    sin.Next();
                    return result;
                }
                if (result > (int64MaxValue >> 7))
                {
                    return (long)VCDiffResult.ERROR;
                }
                result = result << 7;
                sin.Next();
            }
            return (int)VCDiffResult.EOD;
        }

        public static int CalcInt32Length(int v)
        {
            if (v < 0)
            {
                return 0;
            }
            int length = 0;
            do
            {
                v >>= 7;
                ++length;
            } while (v > 0);
            return length;
        }

        public static int CalcInt64Length(long v)
        {
            if (v < 0)
            {
                return 0;
            }
            int length = 0;
            do
            {
                v >>= 7;
                ++length;
            } while (v > 0);
            return length;
        }

        public static int AppendInt32(int v, Stream sout)
        {
            Span<byte> varint = stackalloc byte[int32Max];
            int length = EncodeInt32(v, varint);
            int start = int32Max - length;
            sout.Write(varint[start..int32Max]);
            return length;
        }

        public static int AppendInt64(long v, Stream sout)
        {
            Span<byte> varint = stackalloc byte[int64Max];
            int length = EncodeInt64(v, varint);
            int start = int64Max - length;
            sout.Write(varint[start..int64Max]);
            return length;
        }

        //v cannot be negative!
        //the buffer must be of size: int32Max
        public static int EncodeInt32(int v, Span<byte> sout)
        {
            if (v < 0)
            {
                return 0;
            }

            int length = 1;
            int idx = int32Max - 1;
            sout[idx] = (byte)(v & 0x7F);
            --idx;
            v >>= 7;
            while (v > 0)
            {
                sout[idx] = (byte)((v & 0x7F) | 0x80);
                --idx;
                ++length;
                v >>= 7;
            }

            return length;
        }

        //v cannot be negative!
        //the buffer must be of size: int64Max
        public static int EncodeInt64(long v, Span<byte> sout)
        {
            if (v < 0)
            {
                return 0;
            }

            int length = 1;
            int idx = int64Max - 1;
            sout[idx] = (byte)(v & 0x7F);
            --idx;
            v >>= 7;
            while (v > 0)
            {
                sout[idx] = (byte)((v & 0x7F) | 0x80);
                --idx;
                ++length;
                v >>= 7;
            }

            return length;
        }
    }
}