using System.Runtime.InteropServices;

namespace Adamantium.Core;

/// <summary>
/// P/Invoke into the C standard library for POSIX calls used on Linux and macOS. The library name "libc" is
/// resolved by the .NET runtime on both: <c>libc.so.6</c> on Linux and <c>libc.dylib</c> (libSystem) on macOS.
/// Windows code paths must not call these.
/// </summary>
public static unsafe class PosixInterop
{
    private const string LibC = "libc";

    public const short PollIn = 0x001;

    public const int ProtRead = 1;

    public const int MapPrivate = 2;

    public const int EventFdNonBlock = 0x800;

    public const int EventFdCloseOnExec = 0x80000;

    public const int OpenCloseOnExec = 0x80000;

    public const uint MemFdCloseOnExec = 0x1;

    public static readonly nint MapFailed = -1;

    /// <summary>POSIX <c>close(2)</c>: closes a file descriptor (e.g. an exported Vulkan memory/semaphore fd).</summary>
    [DllImport(LibC, EntryPoint = "close", SetLastError = true)]
    public static extern int Close(int fd);

    /// <summary>POSIX <c>dup(2)</c>: a second descriptor for the same file - for a consumer that must hand over a
    /// descriptor it does not own, as a Vulkan fd import takes the one it is given.</summary>
    [DllImport(LibC, EntryPoint = "dup", SetLastError = true)]
    public static extern int Dup(int fd);

    [DllImport(LibC, EntryPoint = "read", SetLastError = true)]
    public static extern nint Read(int fd, void* buffer, nuint count);

    [DllImport(LibC, EntryPoint = "write", SetLastError = true)]
    public static extern nint Write(int fd, void* buffer, nuint count);

    [DllImport(LibC, EntryPoint = "poll", SetLastError = true)]
    public static extern int Poll(PosixPollFd* fds, nuint count, int timeout);

    [DllImport(LibC, EntryPoint = "pipe2", SetLastError = true)]
    public static extern int Pipe2(int* fds, int flags);

    [DllImport(LibC, EntryPoint = "eventfd", SetLastError = true)]
    public static extern int EventFd(uint initial, int flags);

    [DllImport(LibC, EntryPoint = "memfd_create", SetLastError = true)]
    public static extern int MemFdCreate(string name, uint flags);

    [DllImport(LibC, EntryPoint = "mmap", SetLastError = true)]
    public static extern nint Mmap(nint address, nuint length, int protection, int flags, int fd, long offset);

    [DllImport(LibC, EntryPoint = "munmap", SetLastError = true)]
    public static extern int Munmap(nint address, nuint length);
}
