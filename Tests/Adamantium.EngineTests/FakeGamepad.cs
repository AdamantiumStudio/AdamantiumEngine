using Adamantium.Multiverse.Input;

namespace Adamantium.EngineTests;

/// <summary>A gamepad whose reading the test sets, and which keeps how it was last told to vibrate.</summary>
public class FakeGamepad : Gamepad
{
    public GamepadState State;

    public float LowFrequency { get; private set; }

    public float HighFrequency { get; private set; }

    public int VibrationsSet { get; private set; }

    public override GamepadState GetState()
    {
        return State;
    }

    public override void SetVibration(float lowFrequency, float highFrequency)
    {
        LowFrequency = lowFrequency;
        HighFrequency = highFrequency;
        VibrationsSet++;
    }
}
