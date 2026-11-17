using System;

namespace Adamantium.DarkMatter.Dynamics;

/// <summary>How a soft constraint behaves over one substep: the spring of a frequency and damping, as the share of
/// the error it removes and how much of the mass and the last impulse it takes.</summary>
internal readonly struct Softness
{
    private Softness(float biasRate, float massScale, float impulseScale)
    {
        BiasRate = biasRate;
        MassScale = massScale;
        ImpulseScale = impulseScale;
    }

    public float BiasRate { get; }

    public float MassScale { get; }

    public float ImpulseScale { get; }

    public static Softness Create(float hertz, float dampingRatio, float substep)
    {
        if (hertz <= 0)
            return new Softness(0, 1, 0);

        var omega = 2f * MathF.PI * hertz;
        var first = 2f * dampingRatio + substep * omega;
        var second = substep * omega * first;
        var third = 1f / (1f + second);
        return new Softness(omega / first, second * third, third);
    }
}
