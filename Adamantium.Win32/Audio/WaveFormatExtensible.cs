using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32.Audio;

/// <summary>A <c>WAVEFORMATEXTENSIBLE</c>.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WaveFormatExtensible
{
    private const ushort ExtensibleTag = 0xFFFE;
    private const ushort ExtensionSize = 22;
    private const uint FrontLeftAndRight = 0x3;
    private const uint FrontCenter = 0x4;

    private static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00AA00389B71");

    public ushort FormatTag;
    public ushort Channels;
    public uint SamplesPerSecond;
    public uint AverageBytesPerSecond;
    public ushort BlockAlign;
    public ushort BitsPerSample;
    public ushort Size;
    public ushort ValidBitsPerSample;
    public uint ChannelMask;
    public Guid SubFormat;

    /// <summary>32-bit float samples in one or two channels.</summary>
    public static WaveFormatExtensible Float(int sampleRate, int channels)
    {
        var blockAlign = (ushort)(channels * sizeof(float));
        return new WaveFormatExtensible
        {
            FormatTag = ExtensibleTag,
            Channels = (ushort)channels,
            SamplesPerSecond = (uint)sampleRate,
            AverageBytesPerSecond = (uint)(sampleRate * blockAlign),
            BlockAlign = blockAlign,
            BitsPerSample = 32,
            Size = ExtensionSize,
            ValidBitsPerSample = 32,
            ChannelMask = channels == 1 ? FrontCenter : FrontLeftAndRight,
            SubFormat = FloatSubFormat,
        };
    }
}
