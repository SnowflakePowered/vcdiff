using System;
using System.Collections.Generic;
using System.Text;

namespace VCDiff.Shared
{
    /// <summary>
    /// Which checksum format to output.
    /// </summary>
    public enum ChecksumFormat
    {
        /// <summary>
        /// Do not emit a checksum.
        /// </summary>
        None,
        /// <summary>
        /// Emit a Google compatible SDCH checksum.
        /// </summary>
        SDCH,
        /// <summary>
        /// Emit an Xdelta3 checksum.
        /// </summary>
        Xdelta3
    }
}
