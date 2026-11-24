namespace Adamantium.Multiverse.Remote;

/// <summary>A call to a universe host failed: in the host - its message is the host's reason - or its answer could not
/// be read.</summary>
public sealed class UniverseHostException : Exception
{
    public UniverseHostException(string message) : base(message)
    {
    }

    public UniverseHostException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
