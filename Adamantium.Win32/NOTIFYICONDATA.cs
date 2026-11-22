using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32;

/// <summary>An icon in the notification area (<see cref="Win32Interop.ShellNotifyIcon"/>): the window told what is done
/// to it, by which message, and what it shows. <see cref="Flags"/> say which of the fields are given.</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct NOTIFYICONDATA
{
    public uint Size;
    public IntPtr Window;
    public uint Id;
    public NotifyIconFlags Flags;
    public uint CallbackMessage;
    public IntPtr Icon;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string Tip;

    public uint State;
    public uint StateMask;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string Info;

    /// <summary>With <see cref="NotifyIconMessage.SetVersion"/>, how the window is told: <see cref="NotifyIconVersion4"/>.
    /// </summary>
    public uint Version;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string InfoTitle;

    public uint InfoFlags;
    public Guid Item;
    public IntPtr BalloonIcon;

    /// <summary>The window is told what is done to the icon in the callback's lParam - the event in its low word, the icon's
    /// id in its high one - and where in its wParam, in screen pixels.</summary>
    public const uint NotifyIconVersion4 = 4;

    /// <summary>The icon of <paramref name="window"/> with <paramref name="id"/>, sized as the system expects; the rest
    /// given as <see cref="Flags"/> say.</summary>
    public static NOTIFYICONDATA For(IntPtr window, uint id) => new()
    {
        Size = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        Window = window,
        Id = id,
    };
}

/// <summary>What <see cref="Win32Interop.ShellNotifyIcon"/> does with the icon.</summary>
public enum NotifyIconMessage : uint
{
    Add = 0,
    Modify = 1,
    Delete = 2,
    SetFocus = 3,
    SetVersion = 4,
}

/// <summary>Which fields of <see cref="NOTIFYICONDATA"/> are given.</summary>
[Flags]
public enum NotifyIconFlags : uint
{
    Message = 0x01,
    Icon = 0x02,
    Tip = 0x04,
    State = 0x08,
    Info = 0x10,
    Guid = 0x20,
    RealTime = 0x40,

    /// <summary>With <see cref="NOTIFYICONDATA.NotifyIconVersion4"/>, the standard tip is shown.</summary>
    ShowTip = 0x80,
}

/// <summary>What was done to the icon, in the low word of the callback's lParam beside the mouse messages
/// (<see cref="WindowMessages"/>).</summary>
public static class NotifyIconEvent
{
    /// <summary>Clicked, or picked from the keyboard with Space.</summary>
    public const uint Select = 0x0400;

    /// <summary>Picked from the keyboard with Enter.</summary>
    public const uint KeySelect = 0x0401;
}
