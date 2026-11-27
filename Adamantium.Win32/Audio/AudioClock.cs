using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32.Audio;

/// <summary><c>IAudioClock</c>, what of a stream was played, called through its method table. Released by its owner.
/// </summary>
public readonly unsafe struct AudioClock
{
    internal static readonly Guid InterfaceId = new("CD63314F-3FBA-4A1B-812C-EF96358728E7");

    private readonly void** _self;

    internal AudioClock(void* self)
    {
        _self = (void**)self;
    }

    private void** Table => (void**)*_self;

    /// <summary>The units of <see cref="GetPosition"/> in a second.</summary>
    public int GetFrequency(out ulong frequency)
    {
        ulong value;
        var result = ((delegate* unmanaged<void**, ulong*, int>)Table[3])(_self, &value);
        frequency = value;
        return result;
    }

    /// <param name="position">The device's position, in the clock's units.</param>
    /// <param name="time">When the position was read, in units of 100 nanoseconds.</param>
    public int GetPosition(out ulong position, out ulong time)
    {
        ulong value, at;
        var result = ((delegate* unmanaged<void**, ulong*, ulong*, int>)Table[4])(_self, &value, &at);
        position = value;
        time = at;
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
