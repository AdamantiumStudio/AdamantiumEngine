namespace Adamantium.Multiverse.Input.Evdev;

/// <summary>What the Linux gamepad layout (Documentation/input/gamepad.rst) means in the engine's terms: buttons named by
/// their place, the sticks, the triggers and the hat as the d-pad.</summary>
public static class EvdevMapping
{
    public const ushort EventSync = 0x00;
    public const ushort EventKey = 0x01;
    public const ushort EventAbsolute = 0x03;
    public const ushort SyncReport = 0;
    public const ushort SyncDropped = 3;

    public const ushort ButtonSouth = 0x130;
    public const ushort ButtonTriggerLeft2 = 0x138;
    public const ushort ButtonTriggerRight2 = 0x139;

    public const ushort AxisX = 0x00;
    public const ushort AxisY = 0x01;
    public const ushort AxisZ = 0x02;
    public const ushort AxisRx = 0x03;
    public const ushort AxisRy = 0x04;
    public const ushort AxisRz = 0x05;
    public const ushort AxisGas = 0x09;
    public const ushort AxisBrake = 0x0a;
    public const ushort AxisHatX = 0x10;
    public const ushort AxisHatY = 0x11;

    /// <summary>The engine button of a key code; none for a code the layout does not name.</summary>
    public static GamepadButton Button(ushort code) => code switch
    {
        0x130 => GamepadButton.A,
        0x131 => GamepadButton.B,
        0x134 => GamepadButton.X,
        0x133 => GamepadButton.Y,
        0x136 => GamepadButton.LeftShoulder,
        0x137 => GamepadButton.RightShoulder,
        0x13a => GamepadButton.Back,
        0x13b => GamepadButton.Start,
        0x13c => GamepadButton.Guide,
        0x13d => GamepadButton.LeftThumb,
        0x13e => GamepadButton.RightThumb,
        0x220 => GamepadButton.DpadUp,
        0x221 => GamepadButton.DpadDown,
        0x222 => GamepadButton.DpadLeft,
        0x223 => GamepadButton.DpadRight,
        _ => GamepadButton.None,
    };

    /// <summary>A stick axis from -1 to 1 around the middle of its range.</summary>
    public static float Stick(int value, int minimum, int maximum)
    {
        if (maximum <= minimum)
        {
            return 0;
        }

        var middle = (minimum + maximum) / 2.0;
        return (float)System.Math.Clamp((value - middle) / ((maximum - minimum) / 2.0), -1, 1);
    }

    /// <summary>A trigger axis from 0 to 1.</summary>
    public static float Trigger(int value, int minimum, int maximum) =>
        maximum <= minimum ? 0 : (float)System.Math.Clamp((value - minimum) / (double)(maximum - minimum), 0, 1);

    /// <summary>Whose symbols the device's name suggests are on its face buttons.</summary>
    public static GamepadFace Face(string name)
    {
        var lower = name?.ToLowerInvariant() ?? string.Empty;
        if (lower.Contains("sony") || lower.Contains("playstation") || lower.Contains("dualsense") ||
            lower.Contains("dualshock") || lower.Contains("wireless controller"))
        {
            return GamepadFace.PlayStation;
        }

        if (lower.Contains("nintendo") || lower.Contains("pro controller") || lower.Contains("joy-con"))
        {
            return GamepadFace.Nintendo;
        }

        return lower.Contains("xbox") || lower.Contains("x-box") ? GamepadFace.Xbox : GamepadFace.Unknown;
    }
}
