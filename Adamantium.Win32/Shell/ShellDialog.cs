using System;

namespace Adamantium.Win32.Shell;

/// <summary>The identifiers and flags the shell's file dialogs are addressed by.</summary>
public static class ShellDialog
{
    /// <summary>CLSID_FileSaveDialog.</summary>
    public static readonly Guid ClsidFileSaveDialog = new("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B");

    /// <summary>IID_IFileSaveDialog.</summary>
    public static readonly Guid IidFileSaveDialog = new("84BCCD23-5FDE-4CDB-AEA4-AF64B83D78AB");

    /// <summary>CLSID_FileOpenDialog.</summary>
    public static readonly Guid ClsidFileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");

    /// <summary>IID_IFileOpenDialog.</summary>
    public static readonly Guid IidFileOpenDialog = new("D57C7288-D4AD-4768-BE02-9D969532D960");

    /// <summary>FOS_FILEMUSTEXIST - an open dialog must come back with a file that is there, not a name the user typed.
    /// </summary>
    public const uint FileMustExist = 0x00001000;

    /// <summary>FOS_PATHMUSTEXIST - and in a folder that is there.</summary>
    public const uint PathMustExist = 0x00000800;

    /// <summary>FOS_OVERWRITEPROMPT - ask before replacing a file that is already there. A save dialog sets this
    /// itself, but only until someone calls SetOptions, which replaces the lot.</summary>
    public const uint OverwritePrompt = 0x00000002;

    /// <summary>FOS_FORCEFILESYSTEM - only let the user choose somewhere a path can be opened. Without it the dialog
    /// also offers the parts of the shell namespace that are not files at all.</summary>
    public const uint ForceFileSystem = 0x00000040;

    /// <summary>FOS_PICKFOLDERS - an open dialog chooses a folder instead of a file.</summary>
    public const uint PickFolders = 0x00000020;

    /// <summary>SIGDN_FILESYSPATH - the full path on disk, which is the only name we ever want back.</summary>
    public const uint SigdnFileSysPath = 0x80058000;

    /// <summary>HRESULT_FROM_WIN32(ERROR_CANCELLED) - what Show answers when the user closed the dialog without
    /// choosing. A refusal, not a failure.</summary>
    public const int Canceled = unchecked((int)0x800704C7);
}
