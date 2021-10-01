using System;
using System.Runtime.InteropServices;

namespace VCDiff.Shared
{
    internal struct NativeAllocation : IDisposable
    {
        public IntPtr Address;
        public int Size;
        public bool OwnsAllocation;

        public NativeAllocation(int size)
        {
            Address = Marshal.AllocHGlobal(size);
            Size = size;
            OwnsAllocation = true;
        }

        public NativeAllocation(IntPtr address, int size) : this()
        {
            Address = address;
            Size = size;
            OwnsAllocation = false;
        }

        public unsafe Span<byte> AsSpan() => new Span<byte>((void*) Address, Size);

        public void Dispose()
        {
            if (Address != IntPtr.Zero)
                Marshal.FreeHGlobal(Address);
        }
    }
}