namespace VCDiff.Encoders
{
    /// <summary>Controls the number of buckets allocated in the encoder’s source index.</summary>
    public enum IndexPreference
    {
        /// <summary>Use twice the memory-mode bucket count for 16- and 32-bit indexes to reduce collisions.</summary>
        Speed,
        /// <summary>Use the smallest power of two at least as large as the number of indexed source blocks, with a minimum of 256 buckets.</summary>
        Memory
    }
}
