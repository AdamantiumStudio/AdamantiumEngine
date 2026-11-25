namespace Adamantium.Pulsar.Mixing;

/// <summary>A value on its way to another.</summary>
internal struct Ramp(float value)
{
    private long _left;

    public float Value { get; private set; } = value;

    public float Target { get; private set; } = value;

    public readonly bool IsMoving => _left != 0;

    public readonly float Highest => Math.Max(Value, Target);

    public void Set(float target, long frames)
    {
        Target = target;
        _left = frames;
        if (frames == 0)
        {
            Value = target;
        }
    }

    public void Advance(int frames)
    {
        if (_left <= frames)
        {
            _left = 0;
            Value = Target;
            return;
        }

        Value += (Target - Value) * frames / _left;
        _left -= frames;
    }
}
