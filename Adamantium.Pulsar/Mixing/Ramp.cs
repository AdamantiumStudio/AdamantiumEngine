namespace Adamantium.Pulsar.Mixing;

/// <summary>A value on its way to another.</summary>
internal struct Ramp(float value)
{
    /// <summary>The frames until it is there.</summary>
    public long Left { get; private set; }

    public float Value { get; private set; } = value;

    public float Target { get; private set; } = value;

    public readonly bool IsMoving => Left != 0;

    public readonly float Highest => Math.Max(Value, Target);

    public void Set(float target, long frames)
    {
        Target = target;
        Left = frames;
        if (frames == 0)
        {
            Value = target;
        }
    }

    public void Advance(int frames)
    {
        if (Left <= frames)
        {
            Left = 0;
            Value = Target;
            return;
        }

        Value += (Target - Value) * frames / Left;
        Left -= frames;
    }
}
