using System.Runtime.InteropServices;

namespace Adamantium.Pulsar.Outputs;

/// <summary>The calls of libpulse an output is made of. No callback is given to it: its loop is run on the output's thread,
/// and what came is read between.</summary>
internal static unsafe partial class Pulse
{
    public const int ContextReady = 4;
    public const int ContextFailed = 5;
    public const int StreamReady = 2;
    public const int StreamFailed = 3;

    /// <summary>A server that does not run is not started.</summary>
    public const int NoAutoSpawn = 1;

    public const uint Default = uint.MaxValue;

    private const int FloatLittleEndian = 5;
    private const string Library = "libpulse.so.0";

    [StructLayout(LayoutKind.Sequential)]
    public struct SampleSpec
    {
        private int _format;
        private uint _rate;
        private byte _channels;

        public static SampleSpec Float(AudioFormat format) => new()
        {
            _format = FloatLittleEndian,
            _rate = (uint)format.SampleRate,
            _channels = (byte)format.Channels,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BufferAttributes
    {
        public uint MaximumLength;
        public uint TargetLength;
        public uint Prebuffer;
        public uint MinimumRequest;
        public uint FragmentSize;
    }

    [LibraryImport(Library, EntryPoint = "pa_mainloop_new")]
    public static partial IntPtr CreateLoop();

    [LibraryImport(Library, EntryPoint = "pa_mainloop_free")]
    public static partial void FreeLoop(IntPtr loop);

    [LibraryImport(Library, EntryPoint = "pa_mainloop_get_api")]
    public static partial IntPtr ApiOf(IntPtr loop);

    /// <param name="timeout">The longest <see cref="Poll"/> waits, in microseconds.</param>
    [LibraryImport(Library, EntryPoint = "pa_mainloop_prepare")]
    public static partial int Prepare(IntPtr loop, int timeout);

    [LibraryImport(Library, EntryPoint = "pa_mainloop_poll")]
    public static partial int Poll(IntPtr loop);

    [LibraryImport(Library, EntryPoint = "pa_mainloop_dispatch")]
    public static partial int Dispatch(IntPtr loop);

    [LibraryImport(Library, EntryPoint = "pa_context_new", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr CreateContext(IntPtr api, string name);

    [LibraryImport(Library, EntryPoint = "pa_context_connect")]
    public static partial int ConnectContext(IntPtr context, IntPtr server, int flags, IntPtr spawn);

    [LibraryImport(Library, EntryPoint = "pa_context_get_state")]
    public static partial int StateOfContext(IntPtr context);

    [LibraryImport(Library, EntryPoint = "pa_context_errno")]
    public static partial int ErrorOf(IntPtr context);

    [LibraryImport(Library, EntryPoint = "pa_context_disconnect")]
    public static partial void DisconnectContext(IntPtr context);

    [LibraryImport(Library, EntryPoint = "pa_context_unref")]
    public static partial void ReleaseContext(IntPtr context);

    [LibraryImport(Library, EntryPoint = "pa_stream_new", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr CreateStream(IntPtr context, string name, in SampleSpec spec, IntPtr channelMap);

    [LibraryImport(Library, EntryPoint = "pa_stream_connect_playback")]
    public static partial int ConnectPlayback(IntPtr stream, IntPtr device, in BufferAttributes attributes, int flags,
        IntPtr volume, IntPtr sync);

    [LibraryImport(Library, EntryPoint = "pa_stream_get_state")]
    public static partial int StateOfStream(IntPtr stream);

    [LibraryImport(Library, EntryPoint = "pa_stream_get_buffer_attr")]
    public static partial BufferAttributes* AttributesOf(IntPtr stream);

    [LibraryImport(Library, EntryPoint = "pa_stream_writable_size")]
    public static partial nuint WritableSize(IntPtr stream);

    [LibraryImport(Library, EntryPoint = "pa_stream_begin_write")]
    public static partial int BeginWrite(IntPtr stream, out float* data, ref nuint bytes);

    [LibraryImport(Library, EntryPoint = "pa_stream_cancel_write")]
    public static partial int CancelWrite(IntPtr stream);

    [LibraryImport(Library, EntryPoint = "pa_stream_write")]
    public static partial int Write(IntPtr stream, float* data, nuint bytes, IntPtr free, long offset, int seek);

    /// <summary>Where in the stream it last ran dry; -1 for never.</summary>
    [LibraryImport(Library, EntryPoint = "pa_stream_get_underflow_index")]
    public static partial long UnderflowIndex(IntPtr stream);

    [LibraryImport(Library, EntryPoint = "pa_stream_disconnect")]
    public static partial int DisconnectStream(IntPtr stream);

    [LibraryImport(Library, EntryPoint = "pa_stream_unref")]
    public static partial void ReleaseStream(IntPtr stream);

    public static string TextOf(int error) => Marshal.PtrToStringUTF8(TextOfError(error)) ?? $"error {error}";

    [LibraryImport(Library, EntryPoint = "pa_strerror")]
    private static partial IntPtr TextOfError(int error);
}
