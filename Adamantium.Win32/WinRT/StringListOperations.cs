using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.Win32.WinRT;

/// <summary>Waits for a Windows Runtime operation that gives a list of strings, as a task.</summary>
public static class StringListOperations
{
    /// <summary>The strings <paramref name="operation"/> gives, once it ends; none when it is canceled - through
    /// <paramref name="cancellation"/> too. One that fails throws a <see cref="COMException"/> with Windows' error code.
    /// The operation is closed and let go of either way.</summary>
    public static async Task<IReadOnlyList<string>> ReadAsync(IStringListOperation operation,
        CancellationToken cancellation)
    {
        var handler = new StringListOperationHandler();
        var info = operation as IAsyncInfo;
        try
        {
            StringListOperationHandler.ThrowOnFailure(operation.SetCompleted(handler));
            await using (cancellation.Register(() => info?.Cancel()))
            {
                return await handler.Task.ConfigureAwait(false);
            }
        }
        finally
        {
            info?.Close();
            // Finally: handed to the handler too, the same wrapper is counted twice.
            Marshal.FinalReleaseComObject(operation);
        }
    }
}
