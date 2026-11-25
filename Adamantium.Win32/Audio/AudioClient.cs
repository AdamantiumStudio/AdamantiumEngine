using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32.Audio;

/// <summary><c>IAudioClient</c> in shared mode, called through its method table: no wrapper object stands between, so the
/// audio thread allocates nothing. Released by its owner.</summary>
public readonly unsafe struct AudioClient
{
    /// <summary><c>AUDCLNT_E_DEVICE_INVALIDATED</c>: the device is gone, or its format changed.</summary>
    public const int DeviceInvalidated = unchecked((int)0x88890004);

    private const int SharedMode = 0;

    internal static readonly Guid InterfaceId = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");

    private readonly void** _self;

    internal AudioClient(void* self)
    {
        _self = (void**)self;
    }

    private void** Table => (void**)*_self;

    /// <param name="bufferDuration">In units of 100 nanoseconds.</param>
    public int Initialize(AudioClientFlags flags, long bufferDuration, in WaveFormatExtensible format)
    {
        fixed (WaveFormatExtensible* formatPointer = &format)
        {
            return ((delegate* unmanaged<void**, int, uint, long, long, WaveFormatExtensible*, Guid*, int>)Table[3])(_self,
                SharedMode, (uint)flags, bufferDuration, 0, formatPointer, null);
        }
    }

    public int GetBufferSize(out int frames)
    {
        uint size;
        var result = ((delegate* unmanaged<void**, uint*, int>)Table[4])(_self, &size);
        frames = (int)size;
        return result;
    }

    /// <summary>The frames written and not played yet.</summary>
    public int GetCurrentPadding(out int frames)
    {
        uint padding;
        var result = ((delegate* unmanaged<void**, uint*, int>)Table[6])(_self, &padding);
        frames = (int)padding;
        return result;
    }

    /// <param name="defaultPeriod">In units of 100 nanoseconds.</param>
    /// <param name="minimumPeriod">In units of 100 nanoseconds.</param>
    public int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod)
    {
        long byDefault, minimum;
        var result = ((delegate* unmanaged<void**, long*, long*, int>)Table[9])(_self, &byDefault, &minimum);
        defaultPeriod = byDefault;
        minimumPeriod = minimum;
        return result;
    }

    public int Start() => ((delegate* unmanaged<void**, int>)Table[10])(_self);

    public int Stop() => ((delegate* unmanaged<void**, int>)Table[11])(_self);

    public int SetEventHandle(IntPtr handle) => ((delegate* unmanaged<void**, IntPtr, int>)Table[13])(_self, handle);

    public int GetRenderClient(out AudioRenderClient client)
    {
        var result = GetService(AudioRenderClient.InterfaceId, out var service);
        client = new AudioRenderClient(service);
        return result;
    }

    public int GetClock(out AudioClock clock)
    {
        var result = GetService(AudioClock.InterfaceId, out var service);
        clock = new AudioClock(service);
        return result;
    }

    private int GetService(Guid interfaceId, out void* service)
    {
        void* found = null;
        var result = ((delegate* unmanaged<void**, Guid*, void**, int>)Table[14])(_self, &interfaceId, &found);
        service = found;
        return result;
    }

    public void Release()
    {
        if (_self != null)
        {
            Marshal.Release((IntPtr)_self);
        }
    }
}
