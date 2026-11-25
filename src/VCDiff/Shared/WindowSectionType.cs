namespace VCDiff.Shared
{
    /// <summary>Identifies a data section within an encoded VCDIFF window.</summary>
    public enum WindowSectionType
    {
        /// <summary>No section was selected.</summary>
        Unspecified = 0,
        /// <summary>Literal data used by ADD and RUN instructions.</summary>
        AddRunData,
        /// <summary>Operation codes and explicit instruction lengths.</summary>
        InstructionsAndSizes,
        /// <summary>Encoded addresses for COPY instructions.</summary>
        AddressForCopy
    }
}
