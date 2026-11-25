using System;
using System.IO;
using System.Threading.Tasks;
using VCDiff.Includes;
using VCDiff.Shared;

namespace VCDiff.Encoders
{
    /// <summary>Creates a VCDIFF patch by matching target bytes against a source dictionary.</summary>
    public partial class VcEncoder : IDisposable
    {
        private readonly ByteBuffer oldData;
        private readonly Stream targetData;
        private readonly Stream outputStream;

        /// <summary>Gets or sets the search mode used by the next encode call.</summary>
        /// <value>The default, <see cref="MatchAlgorithm.Bounded"/>, examines up to 256 source candidates per target position.</value>
        public MatchAlgorithm Matcher { get; set; } = MatchAlgorithm.Bounded;

        /// <summary>Gets or sets the bucket sizing preference for the next encode call.</summary>
        /// <value>Defaults to <see cref="IndexPreference.Speed"/>. Both modes use 256 buckets for an 8-bit index.</value>
        /// <remarks>This setting is independent of the match search limit and is not stored in the patch.</remarks>
        public IndexPreference IndexPreference { get; set; } = IndexPreference.Speed;

        private long maxWindowBytes;

        private static readonly byte[] MagicBytes = { 0xD6, 0xC3, 0xC4, 0x00, 0x00 };
        private static readonly byte[] MagicBytesExtended = { 0xD6, 0xC3, 0xC4, (byte)'S', 0x00 };
        private int blockSize;
        private int chunkSize;

        private NativeAllocation<byte> _nativeAllocation;

        /// <summary>Creates a word-hash encoder with 1 MiB target windows.</summary>
        /// <param name="source">Readable dictionary stream, positioned at the beginning. The constructor copies its contents into encoder-owned memory.</param>
        /// <param name="target">Readable, seekable stream containing the desired target. Encoding starts at offset zero.</param>
        /// <param name="outputStream">Writable destination for the patch, starting at its current position.</param>
        /// <param name="blockSize">Width and spacing of indexed source blocks (8, 16, or 32 bytes).</param>
        /// <param name="chunkSize">Minimum COPY length in bytes. Zero uses the selected block width. Any other value must be at least that width.</param>
        /// <remarks>The caller retains ownership of all supplied streams.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The dictionary exceeds <see cref="int.MaxValue"/> bytes, the block size is unsupported, or the COPY minimum is invalid.</exception>
        /// <exception cref="EndOfStreamException">The source ends before its declared length.</exception>
        public VcEncoder(Stream source, Stream target, Stream outputStream, BlockSize blockSize, int chunkSize = 0)
            : this(source, target, outputStream, 1, blockSize, chunkSize)
        {
        }

        /// <summary>Creates a word-hash encoder with the specified target window size.</summary>
        /// <param name="source">Readable dictionary stream, positioned at the beginning. The constructor copies its contents into encoder-owned memory.</param>
        /// <param name="target">Readable, seekable stream containing the desired target. Encoding starts at offset zero.</param>
        /// <param name="outputStream">Writable destination for the patch, starting at its current position.</param>
        /// <param name="maxBufferSize">Target window size in MiB. Values less than one select one MiB.</param>
        /// <param name="blockSize">Width and spacing of indexed source blocks (8, 16, or 32 bytes).</param>
        /// <param name="chunkSize">Minimum COPY length in bytes. Zero uses the selected block width. Any other value must be at least that width.</param>
        /// <remarks>The caller retains ownership of all supplied streams.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The dictionary exceeds <see cref="int.MaxValue"/> bytes, the block size is unsupported, or the COPY minimum is invalid.</exception>
        /// <exception cref="EndOfStreamException">The source ends before its declared length.</exception>
        public VcEncoder(Stream source, Stream target, Stream outputStream, int maxBufferSize, BlockSize blockSize, int chunkSize = 0)
        {
            long sourceLength = source.Length;
            if (sourceLength > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(source), "The source must fit in a .NET span.");
            InitializeOptions(maxBufferSize, blockSize, chunkSize);
            targetData = target;
            this.outputStream = outputStream;
            _nativeAllocation = new NativeAllocation<byte>(sourceLength);
            try
            {
                source.ReadExactly(_nativeAllocation.AsSpan());
                oldData = new ByteBuffer(_nativeAllocation);
            }
            catch
            {
                _nativeAllocation.Dispose();
                throw;
            }
        }

        /// <summary>Creates a word-hash encoder with 1 MiB target windows.</summary>
        /// <param name="buffer">Caller-owned dictionary buffer. Keep its storage valid until encoding finishes.</param>
        /// <param name="target">Readable, seekable stream containing the desired target. Encoding starts at offset zero.</param>
        /// <param name="outputStream">Writable destination for the patch, starting at its current position.</param>
        /// <param name="blockSize">Width and spacing of indexed source blocks (8, 16, or 32 bytes).</param>
        /// <param name="chunkSize">Minimum COPY length in bytes. Zero uses the selected block width. Any other value must be at least that width.</param>
        /// <remarks>The caller retains ownership of all supplied streams.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The dictionary exceeds <see cref="int.MaxValue"/> bytes, the block size is unsupported, or the COPY minimum is invalid.</exception>
        public VcEncoder(ByteBuffer buffer, Stream target, Stream outputStream, BlockSize blockSize, int chunkSize = 0)
            : this(buffer, target, outputStream, 1, blockSize, chunkSize)
        {
        }

        /// <summary>Creates a word-hash encoder with the specified target window size.</summary>
        /// <param name="buffer">Caller-owned dictionary buffer. Keep its storage valid until encoding finishes.</param>
        /// <param name="target">Readable, seekable stream containing the desired target. Encoding starts at offset zero.</param>
        /// <param name="outputStream">Writable destination for the patch, starting at its current position.</param>
        /// <param name="maxBufferSize">Target window size in MiB. Values less than one select one MiB.</param>
        /// <param name="blockSize">Width and spacing of indexed source blocks (8, 16, or 32 bytes).</param>
        /// <param name="chunkSize">Minimum COPY length in bytes. Zero uses the selected block width. Any other value must be at least that width.</param>
        /// <remarks>The caller retains ownership of all supplied streams.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The dictionary exceeds <see cref="int.MaxValue"/> bytes, the block size is unsupported, or the COPY minimum is invalid.</exception>
        public VcEncoder(ByteBuffer buffer, Stream target, Stream outputStream, int maxBufferSize, BlockSize blockSize, int chunkSize = 0)
        {
            if (buffer.Length > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(buffer), "The source must fit in a .NET span.");
            this.oldData = buffer;
            InitializeOptions(maxBufferSize, blockSize, chunkSize);
            targetData = target;
            this.outputStream = outputStream;
        }

        private void InitializeOptions(int maxBufferSize, BlockSize blockSize, int chunkSize)
        {
            if (blockSize != BlockSize.Bytes8 && blockSize != BlockSize.Bytes16 && blockSize != BlockSize.Bytes32)
                throw new ArgumentOutOfRangeException(nameof(blockSize), blockSize, "Choose an 8-, 16-, or 32-byte block.");
            if (chunkSize != 0 && chunkSize < (int)blockSize)
                throw new ArgumentOutOfRangeException(nameof(chunkSize), chunkSize, "The minimum COPY length must be at least the block size.");

            maxWindowBytes = Math.Max(1, maxBufferSize) * (1024L * 1024);

            this.blockSize = (int)blockSize;
            this.chunkSize = chunkSize == 0 ? this.blockSize : chunkSize;
        }

        /// <summary>Writes a VCDIFF patch to the output stream.</summary>
        /// <param name="interleaved">Use the SDCH interleaved instruction layout when true.</param>
        /// <param name="checksumFormat">Optional per-window checksum format. Defaults to <see cref="ChecksumFormat.None"/>.</param>
        /// <param name="progress">Optional receiver for the fraction of the target consumed after each window, from zero to one.</param>
        /// <returns><see cref="VCDiffResult.SUCCESS"/> on completion, or <see cref="VCDiffResult.ERROR"/> for an empty source or target.</returns>
        /// <exception cref="EndOfStreamException">The target ends before its declared length.</exception>
        /// <exception cref="ArgumentException">Interleaving was requested with <see cref="ChecksumFormat.Xdelta3"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><see cref="Matcher"/> or <see cref="IndexPreference"/> contains an unsupported value.</exception>
        public VCDiffResult Encode(bool interleaved = false, ChecksumFormat checksumFormat = ChecksumFormat.None, IProgress<float>? progress = null)
        {
            var algorithm = Matcher;
            var preference = IndexPreference;
            var header = PrepareHeader(interleaved, checksumFormat, algorithm, preference);
            if (header == null) return VCDiffResult.ERROR;
            outputStream.Write(header);

            byte[] buffer = CreateWindowBuffer();
            using var chunker = CreateChunkEncoder(buffer.Length, interleaved, checksumFormat, algorithm, preference);
            while (targetData.Position < targetData.Length)
            {
                int read = targetData.Read(buffer.AsSpan());
                if (read == 0) throw new EndOfStreamException();
                chunker.EncodeChunk(buffer.AsSpan(0, read), outputStream);
                progress?.Report((float)targetData.Position / targetData.Length);
            }
            return VCDiffResult.SUCCESS;
        }

        /// <summary>Asynchronously reads target windows and writes a VCDIFF patch to the output stream.</summary>
        /// <param name="interleaved">Use the SDCH interleaved instruction layout when true.</param>
        /// <param name="checksumFormat">Optional per-window checksum format. Defaults to <see cref="ChecksumFormat.None"/>.</param>
        /// <param name="progress">Optional receiver for the fraction of the target consumed after each window, from zero to one.</param>
        /// <returns>A task returning <see cref="VCDiffResult.SUCCESS"/> on completion, or <see cref="VCDiffResult.ERROR"/> for an empty source or target.</returns>
        /// <remarks>Source matching and window output writes run synchronously.</remarks>
        /// <exception cref="EndOfStreamException">The target ends before its declared length.</exception>
        /// <exception cref="ArgumentException">Interleaving was requested with <see cref="ChecksumFormat.Xdelta3"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><see cref="Matcher"/> or <see cref="IndexPreference"/> contains an unsupported value.</exception>
        public async Task<VCDiffResult> EncodeAsync(bool interleaved = false, ChecksumFormat checksumFormat = ChecksumFormat.None, IProgress<float>? progress = null)
        {
            var algorithm = Matcher;
            var preference = IndexPreference;
            var header = PrepareHeader(interleaved, checksumFormat, algorithm, preference);
            if (header == null) return VCDiffResult.ERROR;
            await outputStream.WriteAsync(header);

            byte[] buffer = CreateWindowBuffer();
            using var chunker = CreateChunkEncoder(buffer.Length, interleaved, checksumFormat, algorithm, preference);
            while (targetData.Position < targetData.Length)
            {
                int read = await targetData.ReadAsync(buffer.AsMemory());
                if (read == 0) throw new EndOfStreamException();
                chunker.EncodeChunk(buffer.AsSpan(0, read), outputStream);
                progress?.Report((float)targetData.Position / targetData.Length);
            }
            return VCDiffResult.SUCCESS;
        }

        private byte[]? PrepareHeader(bool interleaved, ChecksumFormat checksumFormat, MatchAlgorithm algorithm, IndexPreference preference)
        {
            if (algorithm != MatchAlgorithm.Exhaustive && algorithm != MatchAlgorithm.Bounded)
                throw new ArgumentOutOfRangeException(nameof(Matcher));

            if (preference != IndexPreference.Speed && preference != IndexPreference.Memory)
                throw new ArgumentOutOfRangeException(nameof(IndexPreference));

            if (interleaved && checksumFormat == ChecksumFormat.Xdelta3)
                throw new ArgumentException("Interleaved diffs can not have an xdelta3 checksum!");

            if (!targetData.CanRead || targetData.Length == 0 || oldData.Length == 0)
                return null;

            oldData.Position = 0;
            targetData.Position = 0;
            return interleaved || checksumFormat == ChecksumFormat.SDCH ? MagicBytesExtended : MagicBytes;
        }

        private byte[] CreateWindowBuffer()
            => new byte[(int)Math.Min(targetData.Length, Math.Min(maxWindowBytes, int.MaxValue))];

        private ChunkEncoder CreateChunkEncoder(int windowCapacity, bool interleaved, ChecksumFormat checksumFormat, MatchAlgorithm algorithm, IndexPreference preference)
        {
            int limit = algorithm == MatchAlgorithm.Bounded ? 256 : int.MaxValue;
            // A COPY must fit in a single target window.
            var matcher = windowCapacity < chunkSize ? null : WordHashMatcher.Create(oldData, blockSize, limit, preference);
            return new ChunkEncoder(matcher, oldData.Length, chunkSize, checksumFormat, interleaved);
        }

        /// <summary>Releases the encoder’s dictionary copy. Caller-supplied streams and buffers remain open.</summary>
        public void Dispose()
        {
            _nativeAllocation.Dispose();
            _nativeAllocation = default;
        }
    }
}