namespace Adamantium.Multiverse.Remote;

/// <summary>What a universe host tells its viewer first: its process, for the handles it shares, and the GPU it draws
/// on, which the viewer's must be to take them.</summary>
public readonly record struct RemoteHello(int ProcessId, Guid DeviceUuid);
