using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Threading;

namespace Adamantium.Win32.Audio;

/// <summary>Sets an event when another device becomes the default output. Made, and disposed, on a thread with COM
/// initialized.</summary>
[GeneratedComClass]
public sealed unsafe partial class DefaultOutputWatch : IDeviceNotifications, IDisposable
{
    private const int Render = 0;
    private const int Console = 0;

    private readonly EventWaitHandle _changed;
    private readonly StrategyBasedComWrappers _wrappers = new();
    private readonly IntPtr _client;
    private void* _enumerator;
    private bool _disposed;

    public DefaultOutputWatch(EventWaitHandle changed)
    {
        _changed = changed;
        var unknown = _wrappers.GetOrCreateComInterfaceForObject(this, CreateComInterfaceFlags.None);
        var interfaceId = typeof(IDeviceNotifications).GUID;
        Marshal.QueryInterface(unknown, in interfaceId, out _client);
        Marshal.Release(unknown);
        Result = AudioEndpoints.CreateEnumerator(out _enumerator);
        if (Result >= 0)
        {
            Result = ((delegate* unmanaged<void*, IntPtr, int>)AudioEndpoints.TableOf(_enumerator)[6])(_enumerator, _client);
        }
    }

    /// <summary>What Windows answered to the watch; negative where it does not watch.</summary>
    public int Result { get; }

    public void Dispose()
    {
        if (_enumerator != null)
        {
            ((delegate* unmanaged<void*, IntPtr, int>)AudioEndpoints.TableOf(_enumerator)[7])(_enumerator, _client);
            Marshal.Release((IntPtr)_enumerator);
            _enumerator = null;
        }

        if (!_disposed)
        {
            _disposed = true;
            Marshal.Release(_client);
        }
    }

    void IDeviceNotifications.OnDeviceStateChanged(IntPtr device, uint state)
    {
    }

    void IDeviceNotifications.OnDeviceAdded(IntPtr device)
    {
    }

    void IDeviceNotifications.OnDeviceRemoved(IntPtr device)
    {
    }

    void IDeviceNotifications.OnDefaultDeviceChanged(int flow, int role, IntPtr device)
    {
        if (flow == Render && role == Console)
        {
            _changed.Set();
        }
    }

    void IDeviceNotifications.OnPropertyValueChanged(IntPtr device, PropertyKey key)
    {
    }
}
