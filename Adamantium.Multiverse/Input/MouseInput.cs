using Adamantium.Mathematics;

namespace Adamantium.Multiverse.Input;

public record struct MouseInput
{
    public MouseButton Button;

    public InputType InputType;

    public int WheelDelta;

    public Vector2F Delta;

    /// <summary>For a press, how many clicks in a row it makes, as the host counts them: 2 for a double click.</summary>
    public int ClickCount;

    /// <summary>For a wheel turn, where it was turned: over the surface, in its pixels, as
    /// <see cref="InputWormhole.RelativePosition"/> counts them; null when the host does not say.</summary>
    public Vector2F? Position;
}
