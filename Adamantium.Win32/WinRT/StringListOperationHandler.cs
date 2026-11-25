using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Adamantium.Win32.WinRT;

/// <summary>Hears an <see cref="IStringListOperation"/> end and finishes <see cref="Task"/> with its strings - none when it
/// was canceled; a <see cref="COMException"/> with Windows' error code when it failed.</summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class StringListOperationHandler : IStringListOperationHandler
{
    private const int UnknownFailure = unchecked((int)0x80004005);

    private readonly TaskCompletionSource<IReadOnlyList<string>> _done =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The operation's strings, once it ends.</summary>
    public Task<IReadOnlyList<string>> Task => _done.Task;

    public int Invoke(IStringListOperation operation, AsyncStatus status)
    {
        try
        {
            switch (status)
            {
                case AsyncStatus.Completed:
                    _done.TrySetResult(ReadResults(operation));
                    break;
                case AsyncStatus.Canceled:
                    _done.TrySetResult([]);
                    break;
                default:
                    throw Failure(ReadErrorCode(operation));
            }
        }
        catch (Exception exception)
        {
            _done.TrySetException(exception);
        }

        return 0;
    }

    private static int ReadErrorCode(IStringListOperation operation) =>
        operation is IAsyncInfo info && info.GetErrorCode(out var code) == 0 && code != 0 ? code : UnknownFailure;

    private static COMException Failure(int code) =>
        new($"A Windows Runtime operation failed with 0x{code:X8}.", code);

    private static IReadOnlyList<string> ReadResults(IStringListOperation operation)
    {
        ThrowOnFailure(operation.GetResults(out var results));
        if (results == null)
        {
            return [];
        }

        try
        {
            ThrowOnFailure(results.GetSize(out var size));
            var strings = new string[size];
            for (uint index = 0; index < size; index++)
            {
                ThrowOnFailure(results.GetAt(index, out var item));
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

    private static void ThrowOnFailure(int code)
    {
        if (code < 0)
        {
            throw Failure(code);
        }
    }
}
