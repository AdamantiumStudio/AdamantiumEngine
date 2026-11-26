using System;
using System.Runtime.InteropServices;
using Adamantium.MacOS.Foundation;

namespace Adamantium.MacOS.AppKit;

/// <summary>What an application tells the Mac's accessibility - VoiceOver, Zoom, Voice Control - of what changed in its
/// elements: AppKit's notifications by their names. Main thread only.</summary>
public static class NSAccessibility
{
    /// <summary>Another element has the keyboard.</summary>
    public const string FocusedElementChanged = "AXFocusedUIElementChanged";

    /// <summary>An element's value changed.</summary>
    public const string ValueChanged = "AXValueChanged";

    /// <summary>An element's name changed.</summary>
    public const string TitleChanged = "AXTitleChanged";

    /// <summary>Which of a container's children are selected changed.</summary>
    public const string SelectedChildrenChanged = "AXSelectedChildrenChanged";

    /// <summary>What is selected of an element's text changed.</summary>
    public const string SelectedTextChanged = "AXSelectedTextChanged";

    /// <summary>A row of an outline opened.</summary>
    public const string RowExpanded = "AXRowExpanded";

    /// <summary>A row of an outline shut.</summary>
    public const string RowCollapsed = "AXRowCollapsed";

    /// <summary>A menu opened.</summary>
    public const string MenuOpened = "AXMenuOpened";

    /// <summary>A menu shut.</summary>
    public const string MenuClosed = "AXMenuClosed";

    /// <summary>The elements of a window were laid out anew: some came, some went.</summary>
    public const string LayoutChanged = "AXLayoutChanged";

    /// <summary>An element went.</summary>
    public const string ElementDestroyed = "AXUIElementDestroyed";

    /// <summary>Tells the Mac's accessibility <paramref name="notification"/> of <paramref name="element"/>.</summary>
    public static void PostNotification(IntPtr element, string notification)
    {
        if (element == IntPtr.Zero || string.IsNullOrEmpty(notification))
            return;

        var name = NSString.Create(notification);
        try
        {
            Post(element, name);
        }
        finally
        {
            NSObject.Release(name);
        }
    }

    [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit", EntryPoint = "NSAccessibilityPostNotification")]
    private static extern void Post(IntPtr element, IntPtr notification);
}
