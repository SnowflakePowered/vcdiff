using System;
using System.Runtime.InteropServices;

namespace VCDiff.Shared
{
    internal unsafe struct NativeAllocation<T> : IDisposable where T : unmanaged
    {
        public T* Pointer;
        public int NumItems;
        public bool OwnsAllocation;

        public NativeAllocation(int numItems)
        {
            var bytes = (long)numItems * sizeof(T);
            Pointer = (T*) Marshal.AllocHGlobal((IntPtr) bytes);
            NumItems = numItems;
            OwnsAllocation = true;
        }

        public NativeAllocation(IntPtr address, int size) : this()
        {
            Pointer = (T*) address;
            NumItems = size;
            OwnsAllocation = false;
        }

        public unsafe Span<byte> AsSpan() => new Span<byte>((void*) Pointer, NumItems);

        public void Dispose()
        {
            if (Pointer != (void*)0)
                Marshal.FreeHGlobal((IntPtr) Pointer);
        }
    }
}