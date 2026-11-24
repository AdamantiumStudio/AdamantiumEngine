namespace Adamantium.Multiverse.Remote;

/// <summary>A call to a universe host failed in the host: its message is the host's reason.</summary>
public sealed class UniverseHostException : Exception
{
    public UniverseHostException(string message) : base(message)
    {
    }
}
