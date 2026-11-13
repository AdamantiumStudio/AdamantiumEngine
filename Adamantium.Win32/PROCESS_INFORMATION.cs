using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32;

/// <summary>What <see cref="Win32Interop.CreateProcessW"/> started (PROCESS_INFORMATION): handles to the process and its
/// first thread, both the caller's to close, and their ids.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct PROCESS_INFORMATION
{
    public IntPtr Process;
    public IntPtr Thread;
    public int ProcessId;
    public int ThreadId;
}
