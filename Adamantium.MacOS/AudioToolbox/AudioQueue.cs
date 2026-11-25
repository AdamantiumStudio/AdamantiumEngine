using System;
using System.Runtime.InteropServices;

namespace Adamantium.MacOS.AudioToolbox;

/// <summary>An output <c>AudioQueue</c>: the system plays the buffers queued and gives each back once played, on the run
/// loop of the thread that made the queue. It follows the default device by itself.</summary>
public static unsafe class AudioQueue
{
    private const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    /// <summary>kCFRunLoopDefaultMode.</summary>
    public static IntPtr DefaultMode()
    {
        var library = NativeLibrary.Load(CoreFoundation);
        return Marshal.ReadIntPtr(NativeLibrary.GetExport(library, "kCFRunLoopDefaultMode"));
    }

    /// <summary>CFRunLoopGetCurrent.</summary>
    [DllImport(CoreFoundation, EntryPoint = "CFRunLoopGetCurrent")]
    public static extern IntPtr CurrentRunLoop();

    /// <summary>CFRunLoopRunInMode.</summary>
    [DllImport(CoreFoundation, EntryPoint = "CFRunLoopRunInMode")]
    public static extern int RunLoop(IntPtr mode, double seconds, byte returnAfterSource);

    /// <summary>AudioQueueNewOutput.</summary>
    [DllImport(AudioToolbox, EntryPoint = "AudioQueueNewOutput")]
    public static extern int CreateOutput(in AudioStreamDescription format,
        delegate* unmanaged<IntPtr, IntPtr, AudioQueueBuffer*, void> played, IntPtr userData, IntPtr runLoop, IntPtr mode,
        uint flags, out IntPtr queue);

    /// <summary>AudioQueueAllocateBuffer: freed with the queue.</summary>
    [DllImport(AudioToolbox, EntryPoint = "AudioQueueAllocateBuffer")]
    public static extern int AllocateBuffer(IntPtr queue, uint bytes, out AudioQueueBuffer* buffer);

    /// <summary>AudioQueueEnqueueBuffer.</summary>
    [DllImport(AudioToolbox, EntryPoint = "AudioQueueEnqueueBuffer")]
    public static extern int Enqueue(IntPtr queue, AudioQueueBuffer* buffer, uint packets, IntPtr descriptions);

    /// <summary>AudioQueueStart.</summary>
    [DllImport(AudioToolbox, EntryPoint = "AudioQueueStart")]
    public static extern int Start(IntPtr queue, IntPtr time);

    /// <summary>AudioQueueDispose.</summary>
    [DllImport(AudioToolbox, EntryPoint = "AudioQueueDispose")]
    public static extern int Dispose(IntPtr queue, byte immediately);
}
