namespace Adamantium.Pulsar;

/// <summary>How a voice placed in space gets quieter with its distance: as it is up to <see cref="Near"/>, down by its
/// <see cref="Curve"/> from there, and no quieter past <see cref="Far"/>.</summary>
public readonly record struct Attenuation()
{
    public AttenuationCurve Curve { get; init; } = AttenuationCurve.Inverse;

    /// <summary>In metres, more than 0.</summary>
    public float Near { get; init; } = 1;

    /// <summary>In metres, not less than <see cref="Near"/>: where a <see cref="AttenuationCurve.Linear"/> one is silent.
    /// Infinite for no end, but for a linear one.</summary>
    public float Far { get; init; } = float.PositiveInfinity;

    /// <summary>How steeply it falls, 0 or more: 1 is as sound does in the open.</summary>
    public float Rolloff { get; init; } = 1;

    /// <summary>The gain at <paramref name="distance"/> metres.</summary>
    internal float GainAt(float distance)
    {
        var clamped = Math.Clamp(distance, Near, Far);
        return Curve switch
        {
            AttenuationCurve.Inverse => Near / (Near + Rolloff * (clamped - Near)),
            AttenuationCurve.Linear => Math.Clamp(1 - Rolloff * (clamped - Near) / (Far - Near), 0, 1),
            AttenuationCurve.Exponential => MathF.Pow(clamped / Near, -Rolloff),
            _ => 1,
        };
    }

    internal void Check()
    {
        if (!(Near > 0 && float.IsFinite(Near) && Far >= Near && Rolloff >= 0 && float.IsFinite(Rolloff)) ||
            Curve == AttenuationCurve.Linear && !(float.IsFinite(Far) && Far > Near) || !Enum.IsDefined(Curve))
        {
            throw new ArgumentOutOfRangeException(nameof(Attenuation), this, "No such attenuation.");
        }
    }
}

public enum AttenuationCurve
{
    /// <summary>As loud at any distance.</summary>
    None,

    /// <summary><see cref="Attenuation.Near"/> over the distance, for a rolloff of 1: 6 dB down each time it doubles.
    /// </summary>
    Inverse,

    /// <summary>Evenly down to silence at <see cref="Attenuation.Far"/>, for a rolloff of 1.</summary>
    Linear,

    /// <summary>The distance over <see cref="Attenuation.Near"/>, to the power of minus the rolloff.</summary>
    Exponential,
}
