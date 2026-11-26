using System;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.Foundation;

/// <summary>The user's languages.</summary>
public static class NSLocale
{
    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSLocale");
    private static readonly IntPtr PreferredLanguagesSelector = ObjCRuntime.RegisterSelector("preferredLanguages");

    /// <summary>The languages the user chose, most wanted first, as language tags.</summary>
    public static string[] PreferredLanguages => NSArray.ToStrings(NSObject.Send(Class, PreferredLanguagesSelector));
}
