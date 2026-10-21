using System.Runtime.InteropServices;

namespace Adamantium.Wayland.Native;

/// <summary>The part of libxkbcommon a Wayland keyboard needs.</summary>
internal static unsafe class Xkb
{
    private const string Library = "libxkbcommon.so.0";

    public const int KeymapFormatTextV1 = 1;

    public const int StateModsEffective = 1 << 3;

    public const int StateLayoutEffective = 1 << 7;

    [DllImport(Library, EntryPoint = "xkb_context_new")]
    public static extern nint ContextNew(int flags);

    [DllImport(Library, EntryPoint = "xkb_context_unref")]
    public static extern void ContextUnref(nint context);

    [DllImport(Library, EntryPoint = "xkb_keymap_new_from_string")]
    public static extern nint KeymapNewFromString(nint context, byte* text, int format, int flags);

    [DllImport(Library, EntryPoint = "xkb_keymap_unref")]
    public static extern void KeymapUnref(nint keymap);

    [DllImport(Library, EntryPoint = "xkb_keymap_key_repeats")]
    public static extern int KeymapKeyRepeats(nint keymap, uint key);

    [DllImport(Library, EntryPoint = "xkb_keymap_num_layouts")]
    public static extern uint KeymapNumLayouts(nint keymap);

    [DllImport(Library, EntryPoint = "xkb_keymap_key_get_syms_by_level")]
    public static extern int KeymapKeyGetSymsByLevel(nint keymap, uint key, uint layout, uint level, uint** syms);

    [DllImport(Library, EntryPoint = "xkb_keysym_to_utf32")]
    public static extern uint KeysymToUtf32(uint keysym);

    [DllImport(Library, EntryPoint = "xkb_state_new")]
    public static extern nint StateNew(nint keymap);

    [DllImport(Library, EntryPoint = "xkb_state_unref")]
    public static extern void StateUnref(nint state);

    [DllImport(Library, EntryPoint = "xkb_state_update_mask")]
    public static extern int StateUpdateMask(nint state, uint depressedMods, uint latchedMods, uint lockedMods,
        uint depressedLayout, uint latchedLayout, uint lockedLayout);

    [DllImport(Library, EntryPoint = "xkb_state_key_get_one_sym")]
    public static extern uint StateKeyGetOneSym(nint state, uint key);

    [DllImport(Library, EntryPoint = "xkb_state_key_get_utf8")]
    public static extern int StateKeyGetUtf8(nint state, uint key, byte* buffer, nuint size);

    [DllImport(Library, EntryPoint = "xkb_state_mod_name_is_active")]
    public static extern int StateModNameIsActive(nint state, byte* name, int type);
}
