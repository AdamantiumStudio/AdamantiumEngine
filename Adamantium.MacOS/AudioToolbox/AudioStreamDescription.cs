using System.Runtime.InteropServices;

namespace Adamantium.MacOS.AudioToolbox;

/// <summary>An <c>AudioStreamBasicDescription</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct AudioStreamDescription
{
    private const uint LinearPcm = 0x6C70636D;
    private const uint FloatPacked = 1 | 8;

    public double SampleRate;
    public uint FormatId;
    public uint FormatFlags;
    public uint BytesPerPacket;
    public uint FramesPerPacket;
    public uint BytesPerFrame;
    public uint ChannelsPerFrame;
    public uint BitsPerChannel;
    public uint Reserved;

    /// <summary>32-bit float samples, the channels of a frame side by side.</summary>
    public static AudioStreamDescription Float(int sampleRate, int channels) => new()
    {
        SampleRate = sampleRate,
        FormatId = LinearPcm,
        FormatFlags = FloatPacked,
        BytesPerPacket = (uint)(channels * sizeof(float)),
        FramesPerPacket = 1,
        BytesPerFrame = (uint)(channels * sizeof(float)),
        ChannelsPerFrame = (uint)channels,
        BitsPerChannel = 32,
    };
}
