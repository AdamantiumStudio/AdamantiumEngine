using System.Diagnostics;

namespace Adamantium.Pulsar.Outputs;

/// <summary>The default device of the sound server of Linux, PulseAudio or PipeWire in its place. The server moves the
/// stream where the default device changes or goes.</summary>
internal sealed unsafe class PulseOutput(AudioFormat format, TimeSpan buffer) : DeviceOutput(format, buffer)
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(10);

    // A server that hangs is none: the output starts by the clock.
    private static readonly TimeSpan Answer = TimeSpan.FromSeconds(3);
    // How soon a stop is heard while nothing comes.
    private const int LoopMicroseconds = 20_000;

    private IntPtr _loop;
    private IntPtr _context;
    private IntPtr _stream;
    private long _underflow;

    private int FrameBytes => Format.Channels * sizeof(float);

    protected override void PlayToDevice()
    {
        try
        {
            Open();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new DeviceException("There is no libpulse on this system.", e);
        }

        if (IsStopping)
        {
            return;
        }

        PlaysToDevice(Pulse.AttributesOf(_stream)->TargetLength / (uint)FrameBytes);
        while (!IsStopping)
        {
            Fill();
            Iterate();
            CheckAlive();
        }
    }

    private void Open()
    {
        var started = Stopwatch.GetTimestamp();
        _loop = Pulse.CreateLoop();
        _context = Pulse.CreateContext(Pulse.ApiOf(_loop), AppDomain.CurrentDomain.FriendlyName);
        if (Pulse.ConnectContext(_context, IntPtr.Zero, Pulse.NoAutoSpawn, IntPtr.Zero) < 0)
        {
            Fail("connect to the sound server");
        }

        while (Pulse.StateOfContext(_context) != Pulse.ContextReady && !IsStopping)
        {
            Iterate();
            CheckContext();
            CheckAnswered(started);
        }

        if (IsStopping)
        {
            return;
        }

        OpenStream();
        while (Pulse.StateOfStream(_stream) != Pulse.StreamReady && !IsStopping)
        {
            Iterate();
            CheckAlive();
            CheckAnswered(started);
        }

        _underflow = Pulse.UnderflowIndex(_stream);
    }

    private void OpenStream()
    {
        var spec = Pulse.SampleSpec.Float(Format);
        _stream = Pulse.CreateStream(_context, "Pulsar", spec, IntPtr.Zero);
        var attributes = new Pulse.BufferAttributes
        {
            MaximumLength = Pulse.Default,
            TargetLength = BytesIn(Wanted),
            Prebuffer = Pulse.Default,
            MinimumRequest = BytesIn(Period),
            FragmentSize = Pulse.Default,
        };
        if (_stream == IntPtr.Zero ||
            Pulse.ConnectPlayback(_stream, IntPtr.Zero, attributes, 0, IntPtr.Zero, IntPtr.Zero) < 0)
        {
            Fail("open a stream");
        }
    }

    protected override void CloseDevice()
    {
        if (_stream != IntPtr.Zero)
        {
            Pulse.DisconnectStream(_stream);
            Pulse.ReleaseStream(_stream);
            _stream = IntPtr.Zero;
        }

        if (_context != IntPtr.Zero)
        {
            Pulse.DisconnectContext(_context);
            Pulse.ReleaseContext(_context);
            _context = IntPtr.Zero;
        }

        if (_loop != IntPtr.Zero)
        {
            Pulse.FreeLoop(_loop);
            _loop = IntPtr.Zero;
        }
    }

    private void Fill()
    {
        var writable = Pulse.WritableSize(_stream);
        if (writable == nuint.MaxValue)
        {
            Fail("read how much is wanted");
        }

        var left = writable / (nuint)FrameBytes;
        if (left == 0)
        {
            return;
        }

        while (left != 0)
        {
            left -= Math.Min(left, WriteFrames(left));
        }

        var underflow = Pulse.UnderflowIndex(_stream);
        Filled(underflow != _underflow);
        _underflow = underflow;
    }

    // The server may give less room at a time than it wants.
    private nuint WriteFrames(nuint wanted)
    {
        var bytes = wanted * (nuint)FrameBytes;
        if (Pulse.BeginWrite(_stream, out var samples, ref bytes) < 0 || samples == null || bytes < (nuint)FrameBytes)
        {
            Fail("take the buffer");
        }

        var frames = bytes / (nuint)FrameBytes;
        var rendered = false;
        try
        {
            Render(new Span<float>(samples, (int)frames * Format.Channels));
            rendered = true;
        }
        finally
        {
            if (!rendered)
            {
                Pulse.CancelWrite(_stream);
            }
        }

        if (Pulse.Write(_stream, samples, frames * (nuint)FrameBytes, IntPtr.Zero, 0, 0) < 0)
        {
            Fail("give the buffer back");
        }

        return frames;
    }

    private void Iterate()
    {
        if (Pulse.Prepare(_loop, LoopMicroseconds) < 0 || Pulse.Poll(_loop) < 0 || Pulse.Dispatch(_loop) < 0)
        {
            Fail("hear the sound server");
        }
    }

    private static void CheckAnswered(long started)
    {
        if (Stopwatch.GetElapsedTime(started) > Answer)
        {
            throw new DeviceException("The sound server does not answer.");
        }
    }

    private void CheckContext()
    {
        if (Pulse.StateOfContext(_context) >= Pulse.ContextFailed)
        {
            Fail("stay connected to the sound server");
        }
    }

    private void CheckAlive()
    {
        CheckContext();
        if (Pulse.StateOfStream(_stream) >= Pulse.StreamFailed)
        {
            Fail("keep the stream");
        }
    }

    private uint BytesIn(TimeSpan duration) => (uint)(Format.FramesIn(duration) * FrameBytes);

    private void Fail(string doing) =>
        throw new DeviceException($"Could not {doing}: {Pulse.TextOf(Pulse.ErrorOf(_context))}.");
}
