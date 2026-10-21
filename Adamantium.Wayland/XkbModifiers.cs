namespace Adamantium.Wayland;

/// <summary>The modifiers XKB reports as active.</summary>
[Flags]
public enum XkbModifiers
{
    None = 0,
    Shift = 1 << 0,
    Control = 1 << 1,
    Alt = 1 << 2,
    Logo = 1 << 3,
    CapsLock = 1 << 4,
    NumLock = 1 << 5,
}
