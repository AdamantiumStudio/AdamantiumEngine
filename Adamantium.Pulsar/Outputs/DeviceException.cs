namespace Adamantium.Pulsar.Outputs;

/// <summary>There is no device to play to, or it went away.</summary>
internal sealed class DeviceException(string message, Exception inner = null) : Exception(message, inner);
