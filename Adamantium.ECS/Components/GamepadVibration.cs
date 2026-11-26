using Adamantium.MVVM;

namespace Adamantium.ECS.Components;

/// <summary>A vibration of the gamepads: how strong and how fine each motor buzzes, and for how long. Played by the
/// engine's gamepad vibration service, the editor's play button among those who ask it.</summary>
[ViewModel]
[InspectorComponent(ComponentGroups.Input, Icon = "GamepadIcon")]
public partial class GamepadVibration : Component
{
    public GamepadVibration()
    {
        // As the engine's greeting of a gamepad: felt plainly on an Elite Series 2.
        LowFrequency = 0.7f;
        HighFrequency = 0.7f;
        LowSharpness = 1f;
        HighSharpness = 1f;
        Duration = 0.5f;
    }

    /// <summary>How strong the low-frequency motor - the left handle's - buzzes, 0 to 1.</summary>
    [Bindable]
    public partial float LowFrequency { get; set; }

    /// <summary>How strong the high-frequency motor - the right handle's - buzzes, 0 to 1.</summary>
    [Bindable]
    public partial float HighFrequency { get; set; }

    /// <summary>How fine the low-frequency motor buzzes: 0 a deep rumble, 1 a fine buzz; where the gamepad can tell.</summary>
    [Bindable]
    public partial float LowSharpness { get; set; }

    /// <summary>How fine the high-frequency motor buzzes: 0 a deep rumble, 1 a fine buzz; where the gamepad can tell.</summary>
    [Bindable]
    public partial float HighSharpness { get; set; }

    /// <summary>How strong the motor behind the left trigger buzzes, 0 to 1, where there is one.</summary>
    [Bindable]
    public partial float LeftTrigger { get; set; }

    /// <summary>How strong the motor behind the right trigger buzzes, 0 to 1, where there is one.</summary>
    [Bindable]
    public partial float RightTrigger { get; set; }

    /// <summary>How long it lasts, in seconds.</summary>
    [Bindable]
    public partial float Duration { get; set; }
}
