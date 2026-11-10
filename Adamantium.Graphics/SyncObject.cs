using System;
using System.Threading;

namespace Adamantium.Graphics;

public class SyncObject : IDisposable
{
    private Mutex _mutex;
    
    private string SyncObjectName { get; }
    private bool IsSyncEnabled { get; }

    public SyncObject(string name, bool enableSync)
    {
        SyncObjectName = name;
        if (!Mutex.TryOpenExisting(name, out _mutex))
        {
            _mutex = new Mutex(false, name);
        }

        IsSyncEnabled = enableSync;
    }

    public void Wait()
    {
        if (!IsSyncEnabled) return;
        try
        {
            _mutex.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            // Another process died holding it: it is ours now, as a wait that succeeded.
        }
    }

    public void Release()
    {
        if (!IsSyncEnabled) return;
        _mutex.ReleaseMutex();
    }

    public void Dispose()
    {
        _mutex?.Dispose();
    }
}