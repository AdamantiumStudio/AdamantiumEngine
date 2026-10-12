namespace Adamantium.Multiverse.Input;

/// <summary>
/// A gesture of fingers, as axes. Their values have no bounds: the pan in the output's units, the pinch in doublings
/// (1 when the fingers spread twice as far apart, -1 when they close to half), the rotation in degrees.
/// </summary>
public enum GestureAxis
{
    PanX,
    PanY,
    Pinch,
    Rotate
}
