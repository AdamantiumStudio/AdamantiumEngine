namespace Adamantium.Pulsar;

/// <summary>An audio device cannot be opened or played to.</summary>
public sealed class AudioException(string message, Exception inner = null) : Exception(message, inner);
