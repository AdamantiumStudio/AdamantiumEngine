namespace Adamantium.Pulsar;

/// <summary>Which way a voice placed in space sounds (<see cref="VoicePlace.Facing"/>): as loud within the inner cone,
/// <see cref="OuterGain"/> past the outer one, evenly between. The angles are whole cones', in degrees.</summary>
public readonly record struct SoundCone()
{
    public float InnerAngle { get; init; } = 360;

    public float OuterAngle { get; init; } = 360;

    /// <summary>0 to 1.</summary>
    public float OuterGain { get; init; }

    /// <summary>The gain of a listener <paramref name="angle"/> degrees off where it faces.</summary>
    public float GainAt(float angle)
    {
        var inner = InnerAngle / 2;
        var outer = OuterAngle / 2;
        if (angle <= inner)
        {
            return 1;
        }

        return angle >= outer ? OuterGain : 1 + (OuterGain - 1) * (angle - inner) / (outer - inner);
    }

    internal void Check()
    {
        if (!(InnerAngle is >= 0 and <= 360 && OuterAngle >= InnerAngle && OuterAngle <= 360 && OuterGain is >= 0 and <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(SoundCone), this, "No such cone.");
        }
    }
}
