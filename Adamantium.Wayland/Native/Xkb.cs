using System.Runtime.InteropServices;

namespace Adamantium.Wayland.Native;

/// <summary>
/// The part of libxkbcommon a Wayland keyboard needs: the compositor sends its keymap as XKB text, and keys arrive as
/// evdev codes, so what a key means - its keysym, its text, whether it repeats - is xkbcommon's to say.
/// </summary>
internal static unsafe class Xkb
{
    private const string Library = "libxkbcommon.so.0";

    public const int KeymapFormatTextV1 = 1;

    public const int StateModsEffective = 1 << 3;

    public const int StateLayoutEffective = 1 << 7;

    [DllImport(Library)]
    public static extern nint xkb_context_new(int flags);

    [DllImport(Library)]
    public static extern void xkb_context_unref(nint context);

    [DllImport(Library)]
    public static extern nint xkb_keymap_new_from_string(nint context, byte* text, int format, int flags);

    [DllImport(Library)]
    public static extern void xkb_keymap_unref(nint keymap);

    [DllImport(Library)]
    public static extern int xkb_keymap_key_repeats(nint keymap, uint key);

    [DllImport(Library)]
    public static extern uint xkb_keymap_num_layouts(nint keymap);

    [DllImport(Library)]
    public static extern int xkb_keymap_key_get_syms_by_level(nint keymap, uint key, uint layout, uint level, uint** syms);

    [DllImport(Library)]
    public static extern uint xkb_keysym_to_utf32(uint keysym);

    [DllImport(Library)]
    public static extern nint xkb_state_new(nint keymap);

    [DllImport(Library)]
    public static extern void xkb_state_unref(nint state);

    [DllImport(Library)]
    public static extern int xkb_state_update_mask(nint state, uint depressedMods, uint latchedMods, uint lockedMods,
        uint depressedLayout, uint latchedLayout, uint lockedLayout);

    [DllImport(Library)]
    public static extern uint xkb_state_key_get_one_sym(nint state, uint key);

    [DllImport(Library)]
    public static extern int xkb_state_key_get_utf8(nint state, uint key, byte* buffer, nuint size);

    [DllImport(Library)]
    public static extern int xkb_state_mod_name_is_active(nint state, byte* name, int type);
}

/// <summary>The C library calls the Wayland side needs beyond libwayland: mapping the keymap the compositor sends.</summary>
internal static unsafe class LibC
{
    private const string Library = "libc";

    public const int ProtRead = 1;

    public const int MapPrivate = 2;

    public static readonly nint MapFailed = -1;

    [DllImport(Library)]
    public static extern nint mmap(nint address, nuint length, int protection, int flags, int fd, long offset);

    [DllImport(Library)]
    public static extern int munmap(nint address, nuint length);

    [DllImport(Library)]
    public static extern int close(int fd);
}
