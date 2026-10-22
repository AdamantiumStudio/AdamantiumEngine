using Adamantium.Core;
using System.Text;
using Adamantium.Wayland.Native;

namespace Adamantium.Wayland;

/// <summary>A Wayland keyboard's keymap and state: turns an evdev key code into its keysym and text.</summary>
public sealed unsafe class XkbKeyboard : IDisposable
{
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
    private nint composeTable;
    private nint compose;

    private XkbKeyboard(nint context, nint keymap, nint state)
    {
        this.context = context;
        this.keymap = keymap;
        this.state = state;
        composeTable = ComposeTable(context);
        compose = composeTable == 0 ? 0 : Xkb.ComposeStateNew(composeTable, 0);
    }

    /// <summary>The modifiers active since the last <see cref="UpdateModifiers"/>.</summary>
    public XkbModifiers Modifiers { get; private set; }

    /// <summary>Reads the keymap wl_keyboard.keymap sent, and closes its descriptor. Null when the text is not a
    /// keymap XKB can compile.</summary>
    public static XkbKeyboard FromKeymap(int fd, uint size)
    {
        try
        {
            var text = PosixInterop.Mmap(0, size, PosixInterop.ProtRead, PosixInterop.MapPrivate, fd, 0);
            if (text == PosixInterop.MapFailed)
            {
                return null;
            }

            try
            {
                return FromText((byte*)text);
            }
            finally
            {
                PosixInterop.Munmap(text, size);
            }
        }
        finally
        {
            PosixInterop.Close(fd);
        }
    }

    /// <summary>Compiles a keymap from its XKB text.</summary>
    public static XkbKeyboard FromText(string text)
    {
        using var utf8 = new WaylandString(text);
        return FromText(utf8.Pointer);
    }

    private static XkbKeyboard FromText(byte* text)
    {
        var context = Xkb.ContextNew(0);
        if (context == 0)
        {
            return null;
        }

        var keymap = Xkb.KeymapNewFromString(context, text, Xkb.KeymapFormatTextV1, 0);
        if (keymap == 0)
        {
            Xkb.ContextUnref(context);
            return null;
        }

        var state = Xkb.StateNew(keymap);
        if (state == 0)
        {
            Xkb.KeymapUnref(keymap);
            Xkb.ContextUnref(context);
            return null;
        }

        return new XkbKeyboard(context, keymap, state);
    }

    /// <summary>Takes in what wl_keyboard.modifiers sent.</summary>
    public void UpdateModifiers(uint depressed, uint latched, uint locked, uint group)
    {
        Xkb.StateUpdateMask(state, depressed, latched, locked, 0, 0, group);

        var modifiers = XkbModifiers.None;
        Span<byte> name = stackalloc byte[16];
        foreach (var (modifierName, modifier) in ModifierNames)
        {
            var length = Encoding.ASCII.GetBytes(modifierName, name);
            name[length] = 0;
            fixed (byte* pointer = name)
            {
                if (Xkb.StateModNameIsActive(state, pointer, Xkb.StateModsEffective) > 0)
                {
                    modifiers |= modifier;
                }
            }
        }

        Modifiers = modifiers;
    }

    /// <summary>The key's keysym under the current modifiers and layout: XKB_KEY_a, XKB_KEY_A, XKB_KEY_Return.</summary>
    public uint KeySym(uint evdevCode) => Xkb.StateKeyGetOneSym(state, evdevCode + EvdevOffset);

    /// <summary>The text the key types under the current modifiers and layout, empty for one that types none.</summary>
    public string Text(uint evdevCode)
    {
        Span<byte> buffer = stackalloc byte[64];
        fixed (byte* pointer = buffer)
        {
            var length = Xkb.StateKeyGetUtf8(state, evdevCode + EvdevOffset, pointer, (nuint)buffer.Length);
            return length <= 0 ? string.Empty : Encoding.UTF8.GetString(buffer[..Math.Min(length, buffer.Length - 1)]);
        }
    }

    /// <summary>Feeds a pressed key to the compose sequence of the user's locale; <paramref name="text"/> is what a
    /// finished sequence types.</summary>
    public XkbComposeStatus Compose(uint evdevCode, out string text)
    {
        text = string.Empty;
        if (compose == 0 || Xkb.ComposeStateFeed(compose, KeySym(evdevCode)) != Xkb.ComposeFeedAccepted)
        {
            return XkbComposeStatus.None;
        }

        switch (Xkb.ComposeStateGetStatus(compose))
        {
            case Xkb.ComposeComposing:
                return XkbComposeStatus.Composing;
            case Xkb.ComposeComposed:
                Span<byte> buffer = stackalloc byte[64];
                fixed (byte* pointer = buffer)
                {
                    var length = Xkb.ComposeStateGetUtf8(compose, pointer, (nuint)buffer.Length);
                    text = length <= 0 ? string.Empty : Encoding.UTF8.GetString(buffer[..Math.Min(length, buffer.Length - 1)]);
                }

                Xkb.ComposeStateReset(compose);
                return XkbComposeStatus.Composed;
            case Xkb.ComposeCancelled:
                Xkb.ComposeStateReset(compose);
                return XkbComposeStatus.Cancelled;
            default:
                return XkbComposeStatus.None;
        }
    }

    /// <summary>Drops a compose sequence under way: focus moved on.</summary>
    public void ResetCompose()
    {
        if (compose != 0)
        {
            Xkb.ComposeStateReset(compose);
        }
    }

    private static nint ComposeTable(nint context)
    {
        var locale = FirstSet("LC_ALL", "LC_CTYPE", "LANG") ?? "C";
        try
        {
            using var utf8 = new WaylandString(locale);
            return Xkb.ComposeTableNewFromLocale(context, utf8.Pointer, 0);
        }
        catch (EntryPointNotFoundException)
        {
            return 0;
        }
    }

    private static string FirstSet(params string[] names)
    {
        foreach (var name in names)
        {
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Whether the keymap has the key repeat while held. Wayland repeats nothing itself: the client does,
    /// at the rate wl_keyboard.repeat_info gave.</summary>
    public bool Repeats(uint evdevCode) => Xkb.KeymapKeyRepeats(keymap, evdevCode + EvdevOffset) != 0;

    /// <summary>How many layouts the keymap holds: the ones the user switches between.</summary>
    public uint LayoutCount => Xkb.KeymapNumLayouts(keymap);

    /// <summary>The character the key types unshifted in <paramref name="layout"/>, whichever layout is on; 0 for one
    /// that types none.</summary>
    public int Character(uint evdevCode, uint layout)
    {
        uint* syms;
        var count = Xkb.KeymapKeyGetSymsByLevel(keymap, evdevCode + EvdevOffset, layout, 0, &syms);
        return count > 0 ? (int)Xkb.KeysymToUtf32(syms[0]) : 0;
    }

    public void Dispose()
    {
        if (compose != 0)
        {
            Xkb.ComposeStateUnref(compose);
            compose = 0;
        }

        if (composeTable != 0)
        {
            Xkb.ComposeTableUnref(composeTable);
            composeTable = 0;
        }

        if (state != 0)
        {
            Xkb.StateUnref(state);
            state = 0;
        }

        if (keymap != 0)
        {
            Xkb.KeymapUnref(keymap);
            keymap = 0;
        }

        if (context != 0)
        {
            Xkb.ContextUnref(context);
            context = 0;
        }
    }
}
