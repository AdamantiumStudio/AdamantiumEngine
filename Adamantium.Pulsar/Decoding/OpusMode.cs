namespace Adamantium.Pulsar.Decoding;

/// <summary>An Opus frame's layers (RFC 6716, 3.1): SILK, CELT, or both; none before the first frame.</summary>
internal enum OpusMode
{
    None,
    Silk,
    Hybrid,
    Celt,
}
