using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32.Audio;

/// <summary><c>IAudioRenderClient</c>, called through its method table. Released by its owner.</summary>
public readonly unsafe struct AudioRenderClient
{
    internal static readonly Guid InterfaceId = new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");

    private readonly void** _self;

    internal AudioRenderClient(void* self)
    {
        _self = (void**)self;
    }

    private void** Table => (void**)*_self;

    public int GetBuffer(int frames, out float* samples)
    {
        byte* data = null;
        var result = ((delegate* unmanaged<void**, uint, byte**, int>)Table[3])(_self, (uint)frames, &data);
        samples = (float*)data;
        return result;
    }

    public int ReleaseBuffer(int frames) =>
        ((delegate* unmanaged<void**, uint, uint, int>)Table[4])(_self, (uint)frames, 0);

    public void Release()
    {
        if (_self != null)
        {
            Marshal.Release((IntPtr)_self);
        }
    }
}
