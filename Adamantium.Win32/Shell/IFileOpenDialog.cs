using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32.Shell;

/// <summary>
/// The shell's Open dialog (<c>CLSID_FileOpenDialog</c>). The same vtable as <see cref="IFileSaveDialog"/> down to
/// <c>GetResult</c> - both are <c>IModalWindow</c> followed by <c>IFileDialog</c>, and only what each adds of its own
/// at the end differs - but a separate type all the same, because the interface's GUID is what QueryInterface is asked
/// for and a save dialog will not answer to it.
/// </summary>
[ComImport]
[Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IFileOpenDialog
{
    /// <summary>Runs the dialog modally against an owner window, and does not return until it closes. A canceled
    /// dialog is not an error but it is not S_OK either: it answers HRESULT_FROM_WIN32(ERROR_CANCELLED).</summary>
    [PreserveSig]
    int Show(IntPtr owner);

    /// <summary>The kinds of file on offer. Must be set BEFORE the dialog is shown - afterwards it is refused.</summary>
    [PreserveSig]
    int SetFileTypes(uint count, [MarshalAs(UnmanagedType.LPArray)] FilterSpec[] types);

    [PreserveSig]
    int SetFileTypeIndex(uint index);

    [PreserveSig]
    int GetFileTypeIndex(out uint index);

    [PreserveSig]
    int Advise(IntPtr events, out uint cookie);

    [PreserveSig]
    int Unadvise(uint cookie);

    /// <summary>Flags, as a whole - so a caller reads them first and adds to them rather than replacing the defaults
    /// the dialog already carries.</summary>
    [PreserveSig]
    int SetOptions(uint options);

    [PreserveSig]
    int GetOptions(out uint options);

    [PreserveSig]
    int SetDefaultFolder(IShellItem folder);

    [PreserveSig]
    int SetFolder(IShellItem folder);

    [PreserveSig]
    int GetFolder(out IShellItem folder);

    [PreserveSig]
    int GetCurrentSelection(out IShellItem item);

    /// <summary>The name the dialog opens with, extension included.</summary>
    [PreserveSig]
    int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);

    [PreserveSig]
    int GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);

    [PreserveSig]
    int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

    [PreserveSig]
    int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

    [PreserveSig]
    int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);

    /// <summary>What the user chose. Valid only after <see cref="Show"/> returned S_OK.</summary>
    [PreserveSig]
    int GetResult(out IShellItem item);

    [PreserveSig]
    int AddPlace(IShellItem place, int order);

    /// <summary>Appended when the user types a name with no extension. Without the dot.</summary>
    [PreserveSig]
    int SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);

    /// <summary>Closes a dialog that is showing. Not called here, and present because the slot is: everything after it
    /// would sit at the wrong offset without it.</summary>
    [PreserveSig]
    int Close(int result);

    /// <summary>WHICH DIALOG THIS IS, as far as the shell's memory goes. Given one, the size, the position and the last
    /// folder are remembered under it and come back the next time - separately for each purpose, so choosing a picture
    /// does not reopen where a drawing was last saved. Without it every dialog in the application shares one state, and
    /// whatever shape the last one was dragged into is the shape the next one gets.</summary>
    [PreserveSig]
    int SetClientGuid(ref Guid guid);

    [PreserveSig]
    int ClearClientData();

    /// <summary>Not called here; present because the slot is, as <see cref="Close"/>.</summary>
    [PreserveSig]
    int SetFilter(IntPtr filter);

    /// <summary>What the user chose, of a dialog let choose several (<see cref="ShellDialog.AllowMultiSelect"/>). Valid
    /// only after <see cref="Show"/> returned S_OK.</summary>
    [PreserveSig]
    int GetResults(out IShellItemArray items);

    [PreserveSig]
    int GetSelectedItems(out IShellItemArray items);
}
