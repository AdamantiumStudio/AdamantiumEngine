using System;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.Foundation;

/// <summary>Files' URLs to and from paths.</summary>
public static unsafe class NSURL
{
    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSURL");
    private static readonly IntPtr FileUrlSelector = ObjCRuntime.RegisterSelector("fileURLWithPath:");
    private static readonly IntPtr UrlWithStringSelector = ObjCRuntime.RegisterSelector("URLWithString:");
    private static readonly IntPtr FilePathUrlSelector = ObjCRuntime.RegisterSelector("filePathURL");
    private static readonly IntPtr PathSelector = ObjCRuntime.RegisterSelector("path");

    /// <summary>The URL of the file at <paramref name="path"/>, its letters as they are: NSURL's own takes them apart into
    /// letter and accent.</summary>
    public static string FromPath(string path) => new UriBuilder(Uri.UriSchemeFile, string.Empty)
    {
        Path = string.Join('/', Array.ConvertAll(path.Split('/'), Uri.EscapeDataString))
    }.Uri.AbsoluteUri;

    /// <summary>The NSURL of the file at <paramref name="path"/>, autoreleased.</summary>
    public static IntPtr Create(string path) => SendWithText(FileUrlSelector, path);

    /// <summary>The path of an NSURL of a file.</summary>
    public static string PathOf(IntPtr url) => url == IntPtr.Zero ? null : NSString.ToManaged(NSObject.Send(url, PathSelector));

    /// <summary>The path of a file's URL, one that names the file by its number too; null for another URL.</summary>
    public static string ToPath(string url)
    {
        var parsed = SendWithText(UrlWithStringSelector, url);
        var file = parsed == IntPtr.Zero ? IntPtr.Zero : NSObject.Send(parsed, FilePathUrlSelector);
        return file == IntPtr.Zero ? null : NSString.ToManaged(NSObject.Send(file, PathSelector));
    }

    private static IntPtr SendWithText(IntPtr selector, string text)
    {
        var nsString = NSString.Create(text);
        var answer = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)ObjCRuntime.MessageSend)(Class, selector,
            nsString);
        NSObject.Release(nsString);
        return answer;
    }
}
