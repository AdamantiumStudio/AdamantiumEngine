using System;
using System.Runtime.InteropServices;

namespace Adamantium.MacOS.ObjectiveC;

/// <summary>Objective-C blocks that capture nothing: functions the system calls back.</summary>
public static unsafe class ObjCBlock
{
    private const int IsGlobal = 1 << 28;

    private static readonly IntPtr GlobalClass =
        NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libSystem.B.dylib"), "_NSConcreteGlobalBlock");

    private static readonly Descriptor* SharedDescriptor = CreateDescriptor();

    /// <summary>A block that calls <paramref name="function"/>, an unmanaged function taking the block first. It is there
    /// for good: made once for each function.</summary>
    public static IntPtr Create(IntPtr function)
    {
        var block = (Layout*)NativeMemory.AllocZeroed((nuint)sizeof(Layout));
        block->Class = GlobalClass;
        block->Flags = IsGlobal;
        block->Function = function;
        block->Descriptor = SharedDescriptor;
        return (IntPtr)block;
    }

    private static Descriptor* CreateDescriptor()
    {
        var descriptor = (Descriptor*)NativeMemory.AllocZeroed((nuint)sizeof(Descriptor));
        descriptor->Size = (nuint)sizeof(Layout);
        return descriptor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Layout
    {
        public IntPtr Class;
        public int Flags;
        public int Reserved;
        public IntPtr Function;
        public Descriptor* Descriptor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Descriptor
    {
        public nuint Reserved;
        public nuint Size;
    }
}
