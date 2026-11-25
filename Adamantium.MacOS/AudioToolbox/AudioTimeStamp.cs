using System.Runtime.InteropServices;

namespace Adamantium.MacOS.AudioToolbox;

/// <summary>AudioTimeStamp: of it the position alone is read.</summary>
[StructLayout(LayoutKind.Sequential, Size = 64)]
public struct AudioTimeStamp
{
    /// <summary>In frames since the queue started; negative until the device plays the first one.</summary>
    public double SampleTime;
}
