using System;
using System.Runtime.InteropServices;

namespace Adamantium.Engine.Generators;

/// <summary>The system calls that load a native library by path on each platform.</summary>
internal static unsafe class NativeLibraryInterop
{
    /// <summary>dlopen: resolve every symbol now.</summary>
    internal const int RTLD_NOW = 0x002;

    /// <summary>dlopen on Linux: the library's symbols satisfy libraries loaded after it.</summary>
    internal const int LinuxRtldGlobal = 0x100;

    /// <summary>dlopen on macOS: the library's symbols satisfy libraries loaded after it.</summary>
    internal const int MacRtldGlobal = 0x8;

    /// <summary>Windows LoadLibraryW.</summary>
    [DllImport("kernel32", EntryPoint = "LoadLibraryW", ExactSpelling = true, SetLastError = true)]
    internal static extern void* LoadWindowsLibrary(ushort* fileName);

    /// <summary>Linux dlopen.</summary>
    [DllImport("libdl.so.2", EntryPoint = "dlopen")]
    internal static extern IntPtr OpenLinuxLibrary(string fileName, int flags);

    /// <summary>Linux dlerror.</summary>
    [DllImport("libdl.so.2", EntryPoint = "dlerror")]
    internal static extern IntPtr GetLinuxError();

    /// <summary>macOS dlopen.</summary>
    [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "dlopen")]
    internal static extern IntPtr OpenMacLibrary(string fileName, int flags);

    /// <summary>macOS dlerror.</summary>
    [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "dlerror")]
    internal static extern IntPtr GetMacError();
}
