using System.Runtime.InteropServices;

namespace Adamantium.Core;

/// <summary>POSIX <c>struct pollfd</c>: a descriptor and the events to wait for on it.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct PosixPollFd
{
    public int Fd;
    public short Events;
    public short Revents;
}
