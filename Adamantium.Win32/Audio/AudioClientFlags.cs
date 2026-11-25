using System;

namespace Adamantium.Win32.Audio;

/// <summary>The <c>AUDCLNT_STREAMFLAGS</c> a shared stream is opened with.</summary>
[Flags]
public enum AudioClientFlags : uint
{
    None = 0,
    EventCallback = 0x00040000,
    NoPersist = 0x00080000,
    ConvertQuality = 0x08000000,
    ConvertFormat = 0x80000000,
}
