using System.Text;
using Adamantium.Wayland.Native;

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

/// <summary>
/// A Wayland keyboard's keymap and state. wl_keyboard sends the keymap as a file descriptor of XKB text, then keys as
/// evdev codes and the modifiers as masks; this turns a code into its keysym and its text under the current
/// modifiers and layout.
/// <para>Codes are the evdev ones wl_keyboard.key carries - KEY_A is 30 - not XKB's, which are 8 more.</para>
/// </summary>
public sealed unsafe class XkbKeyboard : IDisposable
{
    // XKB numbers keys as X11 does: the evdev code plus 8.
    private const uint EvdevOffset = 8;

    private static readonly (string Name, XkbModifiers Modifier)[] ModifierNames =
    [
        ("Shift", XkbModifiers.Shift),
        ("Control", XkbModifiers.Control),
        ("Mod1", XkbModifiers.Alt),
        ("Mod4", XkbModifiers.Logo),
        ("Lock", XkbModifiers.CapsLock),
        ("Mod2", XkbModifiers.NumLock),
    ];

    private nint context;
    private nint keymap;
    private nint state;

    private XkbKeyboard(nint context, nint keymap, nint state)
    {
        this.context = context;
        this.keymap = keymap;
        this.state = state;
    }

    /// <summary>The modifiers active since the last <see cref="UpdateModifiers"/>.</summary>
    public XkbModifiers Modifiers { get; private set; }

    /// <summary>Reads the keymap wl_keyboard.keymap sent, and closes its descriptor. Null when the text is not a
    /// keymap XKB can compile.</summary>
    public static XkbKeyboard? FromKeymap(int fd, uint size)
    {
        try
        {
            var text = LibC.mmap(0, size, LibC.ProtRead, LibC.MapPrivate, fd, 0);
            if (text == LibC.MapFailed)
            {
                return null;
            }

            try
            {
                return FromText((byte*)text);
            }
            finally
            {
                LibC.munmap(text, size);
            }
        }
        finally
        {
            LibC.close(fd);
        }
    }

    /// <summary>Compiles a keymap from its XKB text.</summary>
    public static XkbKeyboard? FromText(string text)
    {
        using var utf8 = new WaylandString(text);
        return FromText(utf8.Pointer);
    }

    private static XkbKeyboard? FromText(byte* text)
    {
        var context = Xkb.xkb_context_new(0);
        if (context == 0)
        {
            return null;
        }

        var keymap = Xkb.xkb_keymap_new_from_string(context, text, Xkb.KeymapFormatTextV1, 0);
        if (keymap == 0)
        {
            Xkb.xkb_context_unref(context);
            return null;
        }

        var state = Xkb.xkb_state_new(keymap);
        if (state == 0)
        {
            Xkb.xkb_keymap_unref(keymap);
            Xkb.xkb_context_unref(context);
            return null;
        }

        return new XkbKeyboard(context, keymap, state);
    }

    /// <summary>Takes in what wl_keyboard.modifiers sent.</summary>
    public void UpdateModifiers(uint depressed, uint latched, uint locked, uint group)
    {
        Xkb.xkb_state_update_mask(state, depressed, latched, locked, 0, 0, group);

        var modifiers = XkbModifiers.None;
        Span<byte> name = stackalloc byte[16];
        foreach (var (modifierName, modifier) in ModifierNames)
        {
            var length = Encoding.ASCII.GetBytes(modifierName, name);
            name[length] = 0;
            fixed (byte* pointer = name)
            {
                if (Xkb.xkb_state_mod_name_is_active(state, pointer, Xkb.StateModsEffective) > 0)
                {
                    modifiers |= modifier;
                }
            }
        }

        Modifiers = modifiers;
    }

    /// <summary>The key's keysym under the current modifiers and layout: XKB_KEY_a, XKB_KEY_A, XKB_KEY_Return.</summary>
    public uint KeySym(uint evdevCode) => Xkb.xkb_state_key_get_one_sym(state, evdevCode + EvdevOffset);

    /// <summary>The text the key types under the current modifiers and layout, empty for one that types none.</summary>
    public string Text(uint evdevCode)
    {
        Span<byte> buffer = stackalloc byte[64];
        fixed (byte* pointer = buffer)
        {
            var length = Xkb.xkb_state_key_get_utf8(state, evdevCode + EvdevOffset, pointer, (nuint)buffer.Length);
            return length <= 0 ? string.Empty : Encoding.UTF8.GetString(buffer[..Math.Min(length, buffer.Length - 1)]);
        }
    }

    /// <summary>Whether the keymap has the key repeat while held. Wayland repeats nothing itself: the client does,
    /// at the rate wl_keyboard.repeat_info gave.</summary>
    public bool Repeats(uint evdevCode) => Xkb.xkb_keymap_key_repeats(keymap, evdevCode + EvdevOffset) != 0;

    public void Dispose()
    {
        if (state != 0)
        {
            Xkb.xkb_state_unref(state);
            state = 0;
        }

        if (keymap != 0)
        {
            Xkb.xkb_keymap_unref(keymap);
            keymap = 0;
        }

        if (context != 0)
        {
            Xkb.xkb_context_unref(context);
            context = 0;
        }
    }
}
