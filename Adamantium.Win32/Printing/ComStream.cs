using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using STATSTG = System.Runtime.InteropServices.ComTypes.STATSTG;

namespace Adamantium.Win32.Printing;

/// <summary>A .NET stream as a COM <c>IStream</c>, for the calls that read or write through one - a PrintTicket, a
/// picture in an XPS package, a job printed to a file. The stream stays the caller's to dispose.</summary>
public sealed class ComStream(Stream stream) : IStream
{
    private const int StgENotImplemented = unchecked((int)0x80030001);

    public Stream Stream { get; } = stream ?? throw new ArgumentNullException(nameof(stream));

    public void Read(byte[] buffer, int count, IntPtr bytesRead)
    {
        var total = 0;
        while (total < count)
        {
            var read = Stream.Read(buffer, total, count - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        if (bytesRead != IntPtr.Zero)
        {
            Marshal.WriteInt32(bytesRead, total);
        }
    }

    public void Write(byte[] buffer, int count, IntPtr bytesWritten)
    {
        Stream.Write(buffer, 0, count);
        if (bytesWritten != IntPtr.Zero)
        {
            Marshal.WriteInt32(bytesWritten, count);
        }
    }

    public void Seek(long offset, int origin, IntPtr newPosition)
    {
        var position = Stream.Seek(offset, (SeekOrigin)origin);
        if (newPosition != IntPtr.Zero)
        {
            Marshal.WriteInt64(newPosition, position);
        }
    }

    public void SetSize(long size) => Stream.SetLength(size);

    public void CopyTo(IStream target, long count, IntPtr bytesRead, IntPtr bytesWritten)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (total < count)
        {
            var read = Stream.Read(buffer, 0, (int)Math.Min(buffer.Length, count - total));
            if (read == 0)
            {
                break;
            }

            target.Write(buffer, read, IntPtr.Zero);
            total += read;
        }

        if (bytesRead != IntPtr.Zero)
        {
            Marshal.WriteInt64(bytesRead, total);
        }

        if (bytesWritten != IntPtr.Zero)
        {
            Marshal.WriteInt64(bytesWritten, total);
        }
    }

    public void Commit(int flags) => Stream.Flush();

    public void Revert() => throw new COMException(null, StgENotImplemented);

    public void LockRegion(long offset, long count, int lockType) => throw new COMException(null, StgENotImplemented);

    public void UnlockRegion(long offset, long count, int lockType) =>
        throw new COMException(null, StgENotImplemented);

    public void Stat(out STATSTG statistics, int flags) =>
        statistics = new STATSTG { type = 2, cbSize = Stream.CanSeek ? Stream.Length : 0 };

    public void Clone(out IStream clone) => throw new COMException(null, StgENotImplemented);
}
