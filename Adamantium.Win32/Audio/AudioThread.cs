using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32.Audio;

/// <summary>What a thread that feeds an audio device asks of Windows: COM, the multimedia scheduler, waiting.</summary>
public static unsafe partial class AudioThread
{
    private const uint Multithreaded = 0;
    private const uint Infinite = 0xFFFFFFFF;

    /// <summary>COM for this thread; a failure leaves nothing to <see cref="LeaveCom"/>.</summary>
    public static int EnterCom() => CoInitializeEx(IntPtr.Zero, Multithreaded);

    public static void LeaveCom() => CoUninitialize();

    /// <summary>Scheduled as Windows schedules audio; zero where the scheduler refuses.</summary>
    public static IntPtr EnterProAudio()
    {
        uint task = 0;
        return AvSetMmThreadCharacteristicsW("Pro Audio", ref task);
    }

    public static void LeaveProAudio(IntPtr registration)
    {
        if (registration != IntPtr.Zero)
        {
            AvRevertMmThreadCharacteristics(registration);
        }
    }

    /// <summary>Waits for either event; the index of the one that came, or -1 on a failure, which
    /// <see cref="Marshal.GetLastPInvokeError"/> tells.</summary>
    public static int WaitForEither(IntPtr first, IntPtr second)
    {
        var handles = stackalloc IntPtr[2] { first, second };
        return (int)WaitForMultipleObjects(2, handles, 0, Infinite);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(IntPtr reserved, uint model);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("avrt.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr AvSetMmThreadCharacteristicsW(string task, ref uint index);

    [LibraryImport("avrt.dll")]
    private static partial int AvRevertMmThreadCharacteristics(IntPtr registration);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForMultipleObjects(uint count, IntPtr* handles, int all, uint milliseconds);
}
