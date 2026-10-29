using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32;

/// <summary>A file operation of the shell (<see cref="Win32Interop.SHFileOperation"/>): what is done, to which paths -
/// each ended by a null, the list by one more - and how.</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct SHFILEOPSTRUCT
{
    public IntPtr Window;
    public uint Function;
    public string From;
    public string To;
    public ushort Flags;
    public int AnyOperationsAborted;
    public IntPtr NameMappings;
    public string ProgressTitle;
}

/// <summary>What <see cref="SHFILEOPSTRUCT"/> does and how.</summary>
public static class ShellFileOperation
{
    public const uint Delete = 3;

    /// <summary>No progress window.</summary>
    public const ushort Silent = 0x0004;

    /// <summary>No question whether to go on.</summary>
    public const ushort NoConfirmation = 0x0010;

    /// <summary>Into the Recycle Bin, where the user can take it back from.</summary>
    public const ushort AllowUndo = 0x0040;

    /// <summary>Asks before deleting for good what the Recycle Bin cannot take - over <see cref="NoConfirmation"/>.
    /// </summary>
    public const ushort WantNukeWarning = 0x4000;
}
