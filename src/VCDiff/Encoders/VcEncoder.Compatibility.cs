using System;
using System.IO;
using VCDiff.Shared;

namespace VCDiff.Encoders
{
    public partial class VcEncoder
    {
        /// <summary>Creates an encoder with the legacy integer settings. Prefer an overload taking <see cref="BlockSize"/>.</summary>
        /// <param name="source">Readable dictionary stream, positioned at the beginning. The constructor copies its contents into encoder-owned memory.</param>
        /// <param name="target">Readable, seekable stream containing the desired target. Encoding starts at offset zero.</param>
        /// <param name="outputStream">Writable destination for the patch, starting at its current position.</param>
        /// <param name="maxBufferSize">Target window size in MiB. Values less than one select one MiB.</param>
        /// <param name="blockSize">Requested block width in bytes. Rounds to the nearest of 8, 16, or 32, with ties rounded down.</param>
        /// <param name="chunkSize">Minimum COPY length in bytes. Values below two select twice the rounded width. Other values are raised to that width when necessary.</param>
        /// <param name="rollingHash">Accepted for binary compatibility and ignored during encoding.</param>
        /// <remarks>The legacy defaults use 16-byte blocks and a 32-byte minimum COPY. Pass <see cref="BlockSize.Bytes16"/> and a chunk size of 32 to preserve those settings.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The dictionary exceeds <see cref="int.MaxValue"/> bytes.</exception>
        /// <exception cref="EndOfStreamException">The source ends before its declared length.</exception>
        [Obsolete("Use the BlockSize overload. To preserve the legacy defaults, pass BlockSize.Bytes16 and chunkSize: 32.")]
        public VcEncoder(Stream source, Stream target, Stream outputStream, int maxBufferSize = 1, int blockSize = 16,
            int chunkSize = 0, RollingHash? rollingHash = null)
            : this(source, target, outputStream, maxBufferSize, NearestBlockSize(blockSize), LegacyMinimumCopyLength(blockSize, chunkSize))
        {
        }

        /// <summary>Creates an encoder with the legacy integer settings. Prefer an overload taking <see cref="BlockSize"/>.</summary>
        /// <param name="buffer">Caller-owned dictionary buffer. Keep its storage valid until encoding finishes.</param>
        /// <param name="target">Readable, seekable stream containing the desired target. Encoding starts at offset zero.</param>
        /// <param name="outputStream">Writable destination for the patch, starting at its current position.</param>
        /// <param name="maxBufferSize">Target window size in MiB. Values less than one select one MiB.</param>
        /// <param name="blockSize">Requested block width in bytes. Rounds to the nearest of 8, 16, or 32, with ties rounded down.</param>
        /// <param name="chunkSize">Minimum COPY length in bytes. Values below two select twice the rounded width. Other values are raised to that width when necessary.</param>
        /// <param name="rollingHash">Accepted for binary compatibility and ignored during encoding.</param>
        /// <remarks>The legacy defaults use 16-byte blocks and a 32-byte minimum COPY. Pass <see cref="BlockSize.Bytes16"/> and a chunk size of 32 to preserve those settings.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The dictionary exceeds <see cref="int.MaxValue"/> bytes.</exception>
        [Obsolete("Use the BlockSize overload. To preserve the legacy defaults, pass BlockSize.Bytes16 and chunkSize: 32.")]
        public VcEncoder(ByteBuffer buffer, Stream target, Stream outputStream, int maxBufferSize = 1, int blockSize = 16,
            int chunkSize = 0, RollingHash? rollingHash = null)
            : this(buffer, target, outputStream, maxBufferSize, NearestBlockSize(blockSize), LegacyMinimumCopyLength(blockSize, chunkSize))
        {
        }

        private static BlockSize NearestBlockSize(int size)
        {
            if (size <= 12) return BlockSize.Bytes8;
            if (size <= 24) return BlockSize.Bytes16;
            return BlockSize.Bytes32;
        }

        private static int LegacyMinimumCopyLength(int blockSize, int chunkSize)
        {
            int width = (int)NearestBlockSize(blockSize);
            return chunkSize < 2 ? 2 * width : Math.Max(width, chunkSize);
        }
    }

    /// <summary>Legacy rolling-hash API. The encoder accepts this type for binary compatibility and ignores the instance.</summary>
    [Obsolete("VcEncoder no longer uses a rolling hash. Omit rollingHash and select a BlockSize overload.")]
    public class RollingHash : IDisposable
    {
        private const ulong Mask = (1UL << 23) - 1;
        private const ulong Multiplier = 257;
        private readonly ulong outgoingFactor;

        /// <summary>Creates a legacy rolling hash for a fixed-size window.</summary>
        /// <param name="size">Window length in bytes.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is zero or negative.</exception>
        public RollingHash(int size)
        {
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size));
            WindowSize = size;

            // A byte leaving the window contributed byte * 257^size after the next shift.
            ulong factor = 1;
            ulong power = Multiplier;
            for (int exponent = size; exponent > 0; exponent >>= 1)
            {
                if ((exponent & 1) != 0)
                    factor = (factor * power) & Mask;
                power = (power * power) & Mask;
            }
            outgoingFactor = factor;
        }

        /// <summary>Gets the size of the rolling window in bytes.</summary>
        public int WindowSize { get; }

        /// <summary>Calculates the next window’s hash after shifting forward by one byte.</summary>
        /// <param name="oldHash">The hash of the preceding window.</param>
        /// <param name="firstByte">The outgoing byte at the start of the preceding window.</param>
        /// <param name="newByte">The incoming byte at the end of the next window.</param>
        /// <returns>The updated 23-bit polynomial hash.</returns>
        public ulong UpdateHash(ulong oldHash, byte firstByte, byte newByte)
            => unchecked(oldHash * Multiplier + newByte - firstByte * outgoingFactor) & Mask;

        /// <summary>Retained for callers that dispose of their hash instance.</summary>
        public void Dispose() { }
    }
}
