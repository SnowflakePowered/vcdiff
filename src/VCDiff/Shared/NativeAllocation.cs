using System;
using System.Runtime.InteropServices;

namespace VCDiff.Shared
{
    internal unsafe struct NativeAllocation : IDisposable
    {
        public byte* Pointer;
        public int Size;
        public bool OwnsAllocation;

        public NativeAllocation(int size)
        {
            Pointer = (byte*) Marshal.AllocHGlobal(size);
            Size = size;
            OwnsAllocation = true;
        }

        public NativeAllocation(IntPtr address, int size) : this()
        {
            Pointer = (byte*) address;
            Size = size;
            OwnsAllocation = false;
        }

        public unsafe Span<byte> AsSpan() => new Span<byte>((void*) Pointer, Size);

        public void Dispose()
        {
            if (Pointer != (void*)0)
                Marshal.FreeHGlobal((IntPtr) Pointer);
        }
    }
}