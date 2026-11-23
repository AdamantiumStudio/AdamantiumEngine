using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Adamantium.Win32.WinRT;

/// <summary>Hears an <see cref="IStringListOperation"/> end and finishes <see cref="Task"/> with its strings - none when it
/// was canceled or failed.</summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class StringListOperationHandler : IStringListOperationHandler
{
    private readonly TaskCompletionSource<IReadOnlyList<string>> _done =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The operation's strings, once it ends.</summary>
    public Task<IReadOnlyList<string>> Task => _done.Task;

    public int Invoke(IStringListOperation operation, AsyncStatus status)
    {
        if (status != AsyncStatus.Completed)
        {
            _done.TrySetResult([]);
            return 0;
        }

        try
        {
            _done.TrySetResult(ReadResults(operation));
        }
        catch (Exception exception)
        {
            _done.TrySetException(exception);
        }

        return 0;
    }

    private static IReadOnlyList<string> ReadResults(IStringListOperation operation)
    {
        if (operation.GetResults(out var results) != 0 || results == null)
        {
            return [];
        }

        try
        {
            if (results.GetSize(out var size) != 0)
            {
                return [];
            }

            var strings = new string[size];
            for (uint index = 0; index < size; index++)
            {
                if (results.GetAt(index, out var item) != 0)
                {
                    return [];
                }

                try
                {
                    strings[index] = WinRTInterop.ReadString(item);
                }
                finally
                {
                    WinRTInterop.DeleteString(item);
                }
            }

            return strings;
        }
        finally
        {
            Marshal.ReleaseComObject(results);
        }
    }
}
