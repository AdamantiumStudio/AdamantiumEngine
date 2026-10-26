using System;
using System.Threading;

namespace Adamantium.Core;

/// <summary>Something disposed once: <see cref="Disposing"/>, then <see cref="Dispose(bool)"/>, then
/// <see cref="Disposed"/>. One left undisposed frees its unmanaged part when it is collected.</summary>
public abstract class DisposableBase : IDisposable
{
    private int _disposed;

    /// <summary>Whether it is disposed - from the moment disposing starts.</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Raised as disposing starts.</summary>
    public event EventHandler Disposing;

    /// <summary>Raised once it is disposed.</summary>
    public event EventHandler Disposed;

    /// <summary>Frees what it holds: the managed part too when <paramref name="disposeManaged"/>, only the unmanaged
    /// one when collected undisposed.</summary>
    protected abstract void Dispose(bool disposeManaged);

    ~DisposableBase()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Disposing?.Invoke(this, EventArgs.Empty);
        Dispose(true);
        GC.SuppressFinalize(this);
        Disposed?.Invoke(this, EventArgs.Empty);
    }
}
