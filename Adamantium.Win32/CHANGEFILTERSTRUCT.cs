using System.Runtime.InteropServices;

namespace Adamantium.Win32;

/// <summary>What <see cref="Messages.ChangeWindowMessageFilterEx"/> found: whether the message already got to the window
/// before the call.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CHANGEFILTERSTRUCT
{
    public uint Size;
    public MessageFilterStatus Status;

    public static CHANGEFILTERSTRUCT Create() => new() { Size = (uint)Marshal.SizeOf<CHANGEFILTERSTRUCT>() };
}

/// <summary>What <see cref="Messages.ChangeWindowMessageFilterEx"/> does to a window's filter of messages from processes
/// of a lower integrity level.</summary>
public enum MessageFilterAction : uint
{
    Reset = 0,
    Allow = 1,
    Disallow = 2
}

/// <summary>The message's state before <see cref="Messages.ChangeWindowMessageFilterEx"/>.</summary>
public enum MessageFilterStatus : uint
{
    None = 0,
    AlreadyAllowedForWindow = 1,
    AlreadyDisallowedForWindow = 2,
    AllowedHigher = 3
}
