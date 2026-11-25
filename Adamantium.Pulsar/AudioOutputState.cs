namespace Adamantium.Pulsar;

public enum AudioOutputState
{
    Stopped,

    Running,

    /// <summary>The device went away, or the renderer failed: <see cref="IAudioOutput.Stop"/> says which by throwing.</summary>
    Failed,
}
