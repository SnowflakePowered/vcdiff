namespace VCDiff.Encoders
{
    /// <summary>Width and spacing of the source blocks indexed during encoding.</summary>
    public enum BlockSize
    {
        /// <summary>Index an eight-byte block every eight bytes.</summary>
        Bytes8 = 8,
        /// <summary>Index a sixteen-byte block every sixteen bytes (the default).</summary>
        Bytes16 = 16,
        /// <summary>Index a thirty-two-byte block every thirty-two bytes.</summary>
        Bytes32 = 32
    }
}
