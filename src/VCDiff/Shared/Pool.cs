using Microsoft.IO;

namespace VCDiff.Shared
{
    internal static class Pool
    {
        const int BlockSize = RecyclableMemoryStreamManager.DefaultBlockSize;
        
        /// <summary>
        /// For large buffers, multiplies the buffer linearly by this amount.
        /// </summary>
        const int LargeBufferMultiple = 1024 * 1024; // 1 MiB

        /// <summary>
        /// ~2 GiB 
        /// </summary>
        const int MaxBufferSize = LargeBufferMultiple * 2047;

        public static RecyclableMemoryStreamManager MemoryStreamManager = new RecyclableMemoryStreamManager(BlockSize, LargeBufferMultiple, MaxBufferSize)
        {
            AggressiveBufferReturn = true,
            ThrowExceptionOnToArray = true
        };
    }
}
