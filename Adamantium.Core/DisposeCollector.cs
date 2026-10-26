using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace Adamantium.Core;

/// <summary>Things to free together: disposables, and native blocks of <see cref="Utilities.AllocateMemory(nuint)"/>.
/// Freed last-in first-out, each once.</summary>
public class DisposeCollector : DisposableBase
{
    private readonly List<object> _items = new();

    /// <summary>How many things are held.</summary>
    public int Count => _items.Count;

    /// <summary>Holds <paramref name="item"/> - an <see cref="IDisposable"/> or an <see cref="IntPtr"/> to native memory -
    /// to free with the rest, once however often it is given; gives it back.</summary>
    public T Collect<T>(T item)
    {
        if (item is not (IDisposable or IntPtr))
        {
            throw new ArgumentException("Only an IDisposable or an IntPtr to native memory is collected.", nameof(item));
        }

        if (!EqualityComparer<T>.Default.Equals(item, default) && !_items.Contains(item))
        {
            _items.Add(item);
        }

        return item;
    }

    /// <summary>Frees <paramref name="item"/> and leaves it default - if it is held here; otherwise leaves it as it is.
    /// </summary>
    public void RemoveAndDispose<T>(ref T item)
    {
        if (Remove(item))
        {
            Free(item);
            item = default;
        }
    }

    /// <summary>Lets go of <paramref name="item"/> without freeing it; whether it was held.</summary>
    public bool Remove<T>(T item) => _items.Remove(item);

    /// <summary>Frees what is held, the last first, each let go of before it is freed - so one that takes another away
    /// leaves nothing freed twice. One that throws does not stop the rest: they are all freed, and then what was
    /// thrown is thrown - the one exception, or an <see cref="AggregateException"/> of several. Disposables are disposed
    /// only when <paramref name="disposeManagedResources"/>; native blocks are freed either way.</summary>
    public void DisposeAndClear(bool disposeManagedResources = true)
    {
        List<Exception> thrown = null;
        while (_items.Count > 0)
        {
            var item = _items[_items.Count - 1];
            _items.RemoveAt(_items.Count - 1);
            if (!disposeManagedResources && item is not IntPtr)
            {
                continue;
            }

            try
            {
                Free(item);
            }
            catch (Exception e)
            {
                (thrown ??= []).Add(e);
            }
        }

        if (thrown?.Count == 1)
        {
            ExceptionDispatchInfo.Capture(thrown[0]).Throw();
        }

        if (thrown != null)
        {
            throw new AggregateException(thrown);
        }
    }

    protected override void Dispose(bool disposeManaged) => DisposeAndClear(disposeManaged);

    private static void Free(object item)
    {
        if (item is IDisposable disposable)
        {
            disposable.Dispose();
        }
        else
        {
            Utilities.FreeMemory((IntPtr)item);
        }
    }
}
