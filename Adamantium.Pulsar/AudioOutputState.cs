namespace Adamantium.Pulsar;

public enum AudioOutputState
{
    Stopped,

    /// <summary>Played as it should be.</summary>
    Running,

    /// <summary>Running by the clock, unheard: there is no device now. It is <see cref="Running"/> again once there is.
    /// </summary>
    Silent,

    /// <summary>The renderer failed: <see cref="IAudioOutput.Stop"/> says how by throwing.</summary>
    Failed,
}
