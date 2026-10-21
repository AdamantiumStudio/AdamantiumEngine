namespace Adamantium.Wayland;

/// <summary>wl_fixed_t: a signed 24.8 fixed-point number, the protocol's coordinates.</summary>
public static class WlFixed
{
    public static double ToDouble(int value) => value / 256.0;

    public static int FromDouble(double value) => (int)Math.Round(value * 256.0);
}
