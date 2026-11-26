namespace Adamantium.Multiverse.Input;

/// <summary>
/// One connected gamepad, as a platform's gamepad backend sees it.
/// </summary>
public abstract class Gamepad
{
    /// <summary>What the system calls the device; empty when it says nothing.</summary>
    public virtual string Name => string.Empty;

    /// <summary>Whose symbols are printed on the face buttons.</summary>
    public virtual GamepadFace Face => GamepadFace.Unknown;

    /// <summary>The buttons the device has.</summary>
    public virtual GamepadButton SupportedButtons => GamepadButton.A | GamepadButton.B | GamepadButton.X |
                                                    GamepadButton.Y | GamepadButton.LeftShoulder |
                                                    GamepadButton.RightShoulder | GamepadButton.Back |
                                                    GamepadButton.Start | GamepadButton.LeftThumb |
                                                    GamepadButton.RightThumb | GamepadButton.DpadUp |
                                                    GamepadButton.DpadDown | GamepadButton.DpadLeft |
                                                    GamepadButton.DpadRight;

    /// <summary>The latest reading, already in the <see cref="GamepadState"/> ranges.</summary>
    public abstract GamepadState GetState();

    /// <summary>Runs the body motors, each 0..1; zeros stop them.</summary>
    public virtual void SetVibration(float lowFrequency, float highFrequency)
    {
    }

    /// <summary>The sharpness of a deep rumble: the low-frequency motor's own.</summary>
    public const float DeepSharpness = 0;

    /// <summary>The sharpness of a fine buzz: the high-frequency motor's own.</summary>
    public const float FineSharpness = 1;

    /// <summary>How fine each body motor buzzes, from <see cref="DeepSharpness"/> to <see cref="FineSharpness"/>, where the
    /// gamepad can tell (Core Haptics); elsewhere each motor buzzes as it is made to.</summary>
    public virtual void SetSharpness(float lowFrequency, float highFrequency)
    {
    }

    /// <summary>Runs the motors behind the triggers, each 0..1, where the gamepad has them.</summary>
    public virtual void SetTriggerVibration(float left, float right)
    {
    }
}
