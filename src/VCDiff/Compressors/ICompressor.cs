using VCDiff.Includes;
using VCDiff.Shared;

namespace VCDiff.Compressors
{
    /// <summary>
    /// A compression method for secondary compression, for use when <see cref="VCDiffCodeFlags.VCDDECOMPRESS"/> is enabled in the header and the appropriate <see cref="VCDiffCompressFlags"/> flag is enabled for the section in the window.
    /// </summary>
    /// <remarks>
    /// Implementations are stateful, and a single instance must be used for the entire file for a single operation (compression or decompression).
    /// </remarks>
    internal interface ICompressor
    {
        PinnedArrayRental Decompress(WindowSectionType windowSectionType, PinnedArrayRental sectionData);
    }
}
