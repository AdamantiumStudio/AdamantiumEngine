namespace Adamantium.Pulsar.Decoding;

/// <summary>What a FLAC frame says of itself, RFC 9639, 9.1.</summary>
/// <param name="Frame">The first frame of the sound it has.</param>
/// <param name="BlockSize">How many frames of the sound it has.</param>
/// <param name="Channels">The channels' arrangement, as written.</param>
/// <param name="BitsPerSample">The bits of a sample: the stream's where the frame does not say.</param>
/// <param name="IsCounted">Its number counts frames of one size, where <paramref name="Frame"/> comes from.</param>
internal readonly record struct FlacFrameHeader(long Frame, int BlockSize, int Channels, int BitsPerSample, bool IsCounted);
