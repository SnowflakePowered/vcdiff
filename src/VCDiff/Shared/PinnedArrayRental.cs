using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace VCDiff.Shared
{
    internal struct PinnedArrayRental : IDisposable
    {
        /// <summary>
        /// The data encapsulated by this rental.
        /// </summary>
        public byte[]? Data { get; private set; }

        /// <summary>
        /// The number of bytes in this object.
        /// </summary>
        public int NumBytes { get; private set; }

        /// <summary>
        /// Converts the data to a span.
        /// </summary>
        public Span<byte> AsSpan() => Data!.AsSpanFast(NumBytes);

        private GCHandle _pin;

        /// <summary>
        /// Converts the data to a span or an empty span.
        /// </summary>
        public Span<byte> AsSpanOrDefault()
        {
            if (Data != null)
                return Data.AsSpanFast(NumBytes);

            return Span<byte>.Empty;
        }

        public PinnedArrayRental(int numBytes)
        {
            NumBytes = numBytes;
            Data = ArrayPool<byte>.Shared.Rent(NumBytes);
            Debug.Assert(Data.Length >= numBytes);
            _pin = GCHandle.Alloc(Data, GCHandleType.Pinned);
        }

        public void Dispose()
        {
            if (Data != null)
            {
                _pin.Free();
                ArrayPool<byte>.Shared.Return(Data, false);
                Data = null;
            }
        }
    }
}
