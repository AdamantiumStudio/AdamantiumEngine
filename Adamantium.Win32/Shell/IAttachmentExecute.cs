using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32.Shell;

/// <summary>The shell's attachment services (<see cref="Win32Interop.ClsidAttachmentServices"/>): opens or saves a file
/// that came from outside as the system's policy says - marked as from the internet, scanned, warned about. Members are
/// in vtable order.</summary>
[ComImport]
[Guid("73DB1241-1E85-4581-8E4F-A81E1D0F8C57")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAttachmentExecute
{
    [PreserveSig]
    int SetClientTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

    /// <summary>The application's own id, under which the system keeps what the user chose ("do not ask again").</summary>
    [PreserveSig]
    int SetClientGuid(ref Guid guid);

    /// <summary>The file on disk, already written there.</summary>
    [PreserveSig]
    int SetLocalPath([MarshalAs(UnmanagedType.LPWStr)] string localPath);

    [PreserveSig]
    int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string fileName);

    [PreserveSig]
    int SetSource([MarshalAs(UnmanagedType.LPWStr)] string source);

    [PreserveSig]
    int SetReferrer([MarshalAs(UnmanagedType.LPWStr)] string referrer);

    /// <summary>0 when the policy lets the file be opened; an error when it forbids it.</summary>
    [PreserveSig]
    int CheckPolicy();

    [PreserveSig]
    int Prompt(IntPtr window, int prompt, out int action);

    /// <summary>Marks the file at the local path as from outside (Mark of the Web) and lets scanners see it.</summary>
    [PreserveSig]
    int Save();

    /// <summary>Opens the file at the local path after the policy, scanners and the system's warning; a null
    /// <paramref name="process"/> pointer runs it without waiting, which needs a message loop.</summary>
    [PreserveSig]
    int Execute(IntPtr window, [MarshalAs(UnmanagedType.LPWStr)] string verb, IntPtr process);

    [PreserveSig]
    int SaveWithUI(IntPtr window);

    [PreserveSig]
    int ClearClientState();
}
