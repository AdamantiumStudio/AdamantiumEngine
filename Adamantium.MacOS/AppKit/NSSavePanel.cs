using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.AppKit;

/// <summary>The system's dialogs that save and open files. Main thread only.</summary>
public static unsafe class NSSavePanel
{
    private const nint Accepted = 1;

    private static readonly IntPtr TypesFramework = NativeLibrary.Load(
        "/System/Library/Frameworks/UniformTypeIdentifiers.framework/UniformTypeIdentifiers");

    private static readonly IntPtr SaveClass = ObjCFrameworks.GetClass("NSSavePanel");
    private static readonly IntPtr OpenClass = ObjCFrameworks.GetClass("NSOpenPanel");
    private static readonly IntPtr SavePanelSelector = ObjCRuntime.RegisterSelector("savePanel");
    private static readonly IntPtr OpenPanelSelector = ObjCRuntime.RegisterSelector("openPanel");
    private static readonly IntPtr SetMessageSelector = ObjCRuntime.RegisterSelector("setMessage:");
    private static readonly IntPtr SetNameSelector = ObjCRuntime.RegisterSelector("setNameFieldStringValue:");
    private static readonly IntPtr SetDirectorySelector = ObjCRuntime.RegisterSelector("setDirectoryURL:");
    private static readonly IntPtr SetContentTypesSelector = ObjCRuntime.RegisterSelector("setAllowedContentTypes:");
    private static readonly IntPtr SetAllowsOtherTypesSelector = ObjCRuntime.RegisterSelector("setAllowsOtherFileTypes:");
    private static readonly IntPtr SetChoosesFilesSelector = ObjCRuntime.RegisterSelector("setCanChooseFiles:");
    private static readonly IntPtr SetChoosesFoldersSelector = ObjCRuntime.RegisterSelector("setCanChooseDirectories:");
    private static readonly IntPtr SetCreatesFoldersSelector = ObjCRuntime.RegisterSelector("setCanCreateDirectories:");
    private static readonly IntPtr SetIdentifierSelector = ObjCRuntime.RegisterSelector("setIdentifier:");
    private static readonly IntPtr RunModalSelector = ObjCRuntime.RegisterSelector("runModal");
    private static readonly IntPtr UrlSelector = ObjCRuntime.RegisterSelector("URL");
    private static readonly IntPtr TypeWithExtensionSelector = ObjCRuntime.RegisterSelector("typeWithFilenameExtension:");

    /// <summary>A new dialog that saves, autoreleased.</summary>
    public static IntPtr CreateSave() => NSObject.Send(SaveClass, SavePanelSelector);

    /// <summary>A new dialog that opens a file, or a folder, autoreleased.</summary>
    public static IntPtr CreateOpen(bool folders)
    {
        var panel = NSObject.Send(OpenClass, OpenPanelSelector);
        NSObject.Send(panel, SetChoosesFilesSelector, !folders);
        NSObject.Send(panel, SetChoosesFoldersSelector, folders);
        NSObject.Send(panel, SetCreatesFoldersSelector, folders);
        return panel;
    }

    /// <summary>Says what is asked above the files.</summary>
    public static void SetMessage(IntPtr panel, string message) => SetText(panel, SetMessageSelector, message);

    /// <summary>Sets the name a file is saved under to begin with.</summary>
    public static void SetName(IntPtr panel, string name) => SetText(panel, SetNameSelector, name);

    /// <summary>Sets the folder the dialog opens in.</summary>
    public static void SetFolder(IntPtr panel, string folder) =>
        NSObject.Send(panel, SetDirectorySelector, NSURL.Create(folder));

    /// <summary>Sets the name under which the system remembers the dialog's folder and size.</summary>
    public static void SetIdentifier(IntPtr panel, string identifier) => SetText(panel, SetIdentifierSelector, identifier);

    /// <summary>Offers only files of these extensions, without the dot; a name may be saved with another all the same.
    /// </summary>
    public static void SetExtensions(IntPtr panel, IEnumerable<string> extensions)
    {
        GC.KeepAlive(TypesFramework);
        var typeClass = ObjCFrameworks.GetClass("UTType");
        var types = new List<IntPtr>();
        foreach (var extension in extensions)
        {
            var nsExtension = NSString.Create(extension);
            var type = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)ObjCRuntime.MessageSend)(typeClass,
                TypeWithExtensionSelector, nsExtension);
            NSObject.Release(nsExtension);
            if (type != IntPtr.Zero)
                types.Add(type);
        }

        if (types.Count == 0)
            return;

        NSObject.Send(panel, SetContentTypesSelector, NSArray.Create(CollectionsMarshal.AsSpan(types)));
        NSObject.Send(panel, SetAllowsOtherTypesSelector, true);
    }

    /// <summary>Shows the dialog and waits for it; the path chosen, null when it was canceled.</summary>
    public static string Run(IntPtr panel) =>
        NSObject.SendForInteger(panel, RunModalSelector) == Accepted ? NSURL.PathOf(NSObject.Send(panel, UrlSelector)) : null;

    private static void SetText(IntPtr panel, IntPtr selector, string text)
    {
        var nsText = NSString.Create(text);
        NSObject.Send(panel, selector, nsText);
        NSObject.Release(nsText);
    }
}
