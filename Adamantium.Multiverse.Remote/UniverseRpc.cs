using MessagePack;
using MessagePack.Resolvers;
using StreamJsonRpc;

namespace Adamantium.Multiverse.Remote;

/// <summary>How both ends of a universe host's connection talk: StreamJsonRpc over the pipe, messages in MessagePack, the
/// engine's types written by their public members.</summary>
internal static class UniverseRpc
{
    /// <summary>The connection over <paramref name="stream"/>, serving <paramref name="localTarget"/>'s methods; not
    /// listening yet.</summary>
    public static JsonRpc Create(Stream stream, object localTarget)
    {
        var formatter = new MessagePackFormatter();
        formatter.SetMessagePackSerializerOptions(
            MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance));
        var rpc = new JsonRpc(new LengthHeaderMessageHandler(stream, stream, formatter));
        rpc.AddLocalRpcTarget(localTarget);
        return rpc;
    }
}
