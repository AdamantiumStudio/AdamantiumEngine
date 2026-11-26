using System;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.Foundation;

/// <summary>Files moved as the Finder moves them.</summary>
public static unsafe class NSFileManager
{
    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSFileManager");
    private static readonly IntPtr DefaultManagerSelector = ObjCRuntime.RegisterSelector("defaultManager");
    private static readonly IntPtr TrashSelector = ObjCRuntime.RegisterSelector("trashItemAtURL:resultingItemURL:error:");

    /// <summary>Moves the file or folder at <paramref name="path"/> to the Trash; false when it stays where it is.</summary>
    public static bool MoveToTrash(string path)
    {
        var url = NSURL.Create(path);
        return ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr*, IntPtr*, byte>)ObjCRuntime.MessageSend)(
            NSObject.Send(Class, DefaultManagerSelector), TrashSelector, url, null, null) != 0;
    }
}
