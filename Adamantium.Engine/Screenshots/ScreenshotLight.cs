using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Screenshots;

/// <summary>A directional light as <see cref="ScreenshotEffect"/> reads it: where it shines, in the world's axes, and
/// its color times its intensity.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct ScreenshotLight
{
    public ScreenshotLight(Vector4F direction, Vector4F color)
    {
        Direction = direction;
        Color = color;
    }

    public Vector4F Direction { get; }

    public Vector4F Color { get; }
}
