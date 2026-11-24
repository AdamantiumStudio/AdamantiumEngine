using MessagePack;

namespace Adamantium.Multiverse.Remote;

/// <summary>What one end of a <see cref="RemoteLink"/> serves: the calls and notices the other end sends, taken on the
/// link's reading thread.</summary>
internal interface IRemoteHandler
{
    /// <summary>A notice: <paramref name="method"/> called with what <paramref name="arguments"/> holds.</summary>
    void Hear(int method, ref MessagePackReader arguments);

    /// <summary>A call: answered later through the link, by <paramref name="id"/>.</summary>
    void Answer(int method, int id, ref MessagePackReader arguments);

    /// <summary>The caller called call <paramref name="id"/> off.</summary>
    void CallOff(int id);
}
