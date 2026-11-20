using System.ComponentModel;
using Adamantium.Win32;

namespace Adamantium.UniverseHost;

/// <summary>Gives the viewer's process copies of this process's handles - the memory and timelines of the surfaces the
/// universe draws into - for it to import. On Windows only, through DuplicateHandle; elsewhere the descriptors go over
/// the socket, which is not done yet.</summary>
internal sealed class HandleTransfer : IDisposable
{
    private readonly IntPtr _viewerProcess;

    public HandleTransfer(int viewerProcessId)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Frames are shared with another process on Windows only, for now.");
        }

        _viewerProcess = Win32Interop.OpenProcess(Win32Interop.ProcessDuplicateHandle, false, viewerProcessId);
        if (_viewerProcess == IntPtr.Zero)
        {
            throw new Win32Exception();
        }
    }

    /// <summary>A copy of <paramref name="handle"/> in the viewer's process, its value there; 0 for none.</summary>
    public long Give(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return 0;
        }

        if (!Win32Interop.DuplicateHandle(Win32Interop.GetCurrentProcess(), handle, _viewerProcess, out var copy, 0, false,
                Win32Interop.DuplicateSameAccess))
        {
            throw new Win32Exception();
        }

        return copy.ToInt64();
    }

    public void Dispose() => Win32Interop.CloseHandle(_viewerProcess);
}
