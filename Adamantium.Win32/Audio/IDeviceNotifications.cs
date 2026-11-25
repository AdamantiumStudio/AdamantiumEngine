using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Adamantium.Win32.Audio;

/// <summary><c>IMMNotificationClient</c>.</summary>
[GeneratedComInterface]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
internal partial interface IDeviceNotifications
{
    void OnDeviceStateChanged(IntPtr device, uint state);

    void OnDeviceAdded(IntPtr device);

    void OnDeviceRemoved(IntPtr device);

    void OnDefaultDeviceChanged(int flow, int role, IntPtr device);

    void OnPropertyValueChanged(IntPtr device, PropertyKey key);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid Format;
    public uint Id;
}
