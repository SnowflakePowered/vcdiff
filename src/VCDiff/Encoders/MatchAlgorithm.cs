namespace VCDiff.Encoders
{
    /// <summary>Selects how the encoder finds source ranges for COPY instructions.</summary>
    public enum MatchAlgorithm
    {
        /// <summary>Exhaustive word-hash search.</summary>
        Exhaustive = 1,
        /// <summary>Word-hash search capped at 256 bucket entries per target position.</summary>
        Bounded = 2
    }
}
