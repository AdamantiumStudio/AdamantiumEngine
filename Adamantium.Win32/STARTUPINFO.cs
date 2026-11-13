using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32;

/// <summary>How <see cref="Win32Interop.CreateProcessW"/> sets up a new process's first window and streams (STARTUPINFOW).
/// <see cref="Size"/> must be set to the struct's size; <see cref="Desktop"/> names the desktop it shows on, as
/// <c>WinSta0\Name</c>, or null for the caller's.</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct STARTUPINFO
{
    public int Size;
    public string Reserved;
    public string Desktop;
    public string Title;
    public int X;
    public int Y;
    public int XSize;
    public int YSize;
    public int XCountChars;
    public int YCountChars;
    public int FillAttribute;
    public int Flags;
    public short ShowWindow;
    public short Reserved2Size;
    public IntPtr Reserved2;
    public IntPtr StandardInput;
    public IntPtr StandardOutput;
    public IntPtr StandardError;
}
