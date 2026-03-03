using System;
using System.Runtime.InteropServices;

namespace VCDiff.Shared
{
    internal unsafe struct NativeAllocation<T> : IDisposable where T : unmanaged
    {
        public T* Pointer;
        public long NumItems;
        public bool OwnsAllocation;

        public NativeAllocation(long numItems)
        {
            var bytes = numItems * sizeof(T);
            Pointer = (T*) Marshal.AllocHGlobal((IntPtr) bytes);
            NumItems = numItems;
            OwnsAllocation = true;
        }

        public NativeAllocation(IntPtr address, long size) : this()
        {
            Pointer = (T*) address;
            NumItems = size;
            OwnsAllocation = false;
        }

        public unsafe Span<byte> AsSpan() => new Span<byte>((void*) Pointer, (int)NumItems);

        public unsafe Span<byte> AsSpan(long offset, int length) => new Span<byte>((void*)(Pointer + offset), length);

        public void Dispose()
        {
            if (Pointer != (void*)0)
                Marshal.FreeHGlobal((IntPtr) Pointer);
        }
    }
}