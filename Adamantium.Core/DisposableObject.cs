using System;

namespace Adamantium.Core;

/// <summary>A named object disposed once, under a lock: <see cref="Disposing"/>, then <see cref="Dispose(bool)"/>,
/// which frees what it was given through <see cref="ToDispose{T}"/>.</summary>
public abstract class DisposableObject : NamedObject, IDisposable
{
    private readonly object _disposeLock = new();

    /// <summary>What is freed with this object.</summary>
    protected DisposeCollector DisposeCollector { get; set; }

    protected internal DisposableObject()
    {
    }

    /// <summary>An object whose name is <paramref name="name"/>, for good.</summary>
    protected DisposableObject(string name) : base(name)
    {
    }

    /// <summary>Whether it is disposed.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Whether it is being disposed now.</summary>
    protected internal bool IsDisposing { get; private set; }

    /// <summary>Raised as disposing starts.</summary>
    public event EventHandler<EventArgs> Disposing;

    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposing = true;
            Disposing?.Invoke(this, EventArgs.Empty);
            Dispose(true);
            IsDisposed = true;
        }
    }

    /// <summary>Frees what it holds - with <paramref name="disposeManagedResources"/>, what it was given to free too.
    /// </summary>
    protected virtual void Dispose(bool disposeManagedResources)
    {
        if (disposeManagedResources)
        {
            DisposeCollector?.Dispose();
            DisposeCollector = null;
        }
    }

    /// <summary>Frees <paramref name="item"/> - an <see cref="IDisposable"/> or native memory - with this object; gives
    /// it back.</summary>
    protected internal T ToDispose<T>(T item)
    {
        if (item == null)
        {
            return default;
        }

        DisposeCollector ??= new DisposeCollector();
        return DisposeCollector.Collect(item);
    }

    /// <summary>Frees <paramref name="item"/> now and leaves it default, if it was to be freed with this object.
    /// </summary>
    protected internal void RemoveAndDispose<T>(ref T item)
    {
        lock (_disposeLock)
        {
            if (item != null)
            {
                DisposeCollector?.RemoveAndDispose(ref item);
            }
        }
    }

    /// <summary>No longer frees <paramref name="item"/> with this object.</summary>
    protected internal void RemoveToDispose<T>(T item)
    {
        lock (_disposeLock)
        {
            if (item != null)
            {
                DisposeCollector?.Remove(item);
            }
        }
    }
}
