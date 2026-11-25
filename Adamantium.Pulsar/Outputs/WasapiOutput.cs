using System.Runtime.InteropServices;
using Adamantium.Win32.Audio;

namespace Adamantium.Pulsar.Outputs;

/// <summary>The default output device of Windows, in shared mode, filled each time the device says it has played some.
/// Windows converts to the device's own format.</summary>
internal sealed class WasapiOutput(AudioFormat format, TimeSpan buffer) : DeviceOutput(format, buffer)
{
    private const int WaitFailed = -1;
    private const int DeviceWantsMore = 2;
    // In seconds. The device reads 10 ms at a time as a rule: less than half of that is no silence.
    private const double LeastSilence = 0.005;
    private const double ClockJitter = 0.001;

    private AudioClient _client;
    private AudioRenderClient _render;
    private AudioClock _clock;
    private int _bufferFrames;
    private double _clockFrequency;
    private double _silence;
    private bool _silent;
    private ulong _silentAt;

    protected override void PlayToDevice()
    {
        var com = AudioThread.EnterCom();
        var proAudio = AudioThread.EnterProAudio();
        try
        {
            using var wantsMore = new AutoResetEvent(false);
            using var changed = new AutoResetEvent(false);
            using var watch = new DefaultOutputWatch(changed);
            Check(watch.Result, "hear of another default device");
            Open(wantsMore);
            Fill();
            Check(_client.Start(), "start the stream");
            PlaysToDevice(_bufferFrames);
            PlayUntilStoppedOr(changed, wantsMore);
            _client.Stop();
        }
        finally
        {
            CloseDevice();
            AudioThread.LeaveProAudio(proAudio);
            if (com >= 0)
            {
                AudioThread.LeaveCom();
            }
        }
    }

    protected override void CloseDevice()
    {
        _clock.Release();
        _render.Release();
        _client.Release();
        _clock = default;
        _render = default;
        _client = default;
    }

    private void Open(AutoResetEvent wantsMore)
    {
        Check(AudioEndpoints.ActivateDefaultOutput(out _client), "open the default output device");
        var waveFormat = WaveFormatExtensible.Float(Format.SampleRate, Format.Channels);
        Check(_client.Initialize(AudioClientFlags.EventCallback | AudioClientFlags.NoPersist |
            AudioClientFlags.ConvertFormat | AudioClientFlags.ConvertQuality, Wanted.Ticks, waveFormat), "open a stream");
        Check(_client.SetEventHandle(wantsMore.SafeWaitHandle.DangerousGetHandle()), "hear the device");
        Check(_client.GetBufferSize(out _bufferFrames), "read the buffer's size");
        Check(_client.GetRenderClient(out _render), "write to the stream");
        Check(_client.GetClock(out _clock), "read the stream's clock");
        Check(_clock.GetFrequency(out var frequency), "read the stream's clock");
        _clockFrequency = frequency;
        _silent = false;
        _silence = 0;
    }

    private void PlayUntilStoppedOr(AutoResetEvent changed, AutoResetEvent wantsMore)
    {
        var stopping = Stopping.SafeWaitHandle.DangerousGetHandle();
        var other = changed.SafeWaitHandle.DangerousGetHandle();
        var more = wantsMore.SafeWaitHandle.DangerousGetHandle();
        while (true)
        {
            // The first of those that came is told: a device that always wants more must not hide the others.
            var came = AudioThread.WaitForAny(stopping, other, more);
            if (came == WaitFailed)
            {
                throw new DeviceException($"The audio device can not be waited for (error {Marshal.GetLastPInvokeError()}).");
            }

            if (came != DeviceWantsMore)
            {
                return;
            }

            Fill();
        }
    }

    private unsafe void Fill()
    {
        Check(_client.GetCurrentPadding(out var padding), "read how much is left");
        var frames = _bufferFrames - padding;
        if (frames == 0)
        {
            return;
        }

        Check(_render.GetBuffer(frames, out var samples), "take the buffer");
        var rendered = 0;
        try
        {
            Render(new Span<float>(samples, frames * Format.Channels));
            rendered = frames;
        }
        finally
        {
            // A renderer that threw left nothing to play.
            var released = _render.ReleaseBuffer(rendered);
            if (rendered != 0)
            {
                Check(released, "give the buffer back");
            }
        }

        Filled(FellSilent());
    }

    // The device's clock counts what it played of the stream: where it falls behind the time, there was nothing to play.
    private bool FellSilent()
    {
        Check(_clock.GetPosition(out var position, out var time), "read the stream's clock");
        if (position == 0)
        {
            return false;
        }

        var behind = time / (double)TimeSpan.TicksPerSecond - position / _clockFrequency;
        // The start of a stream is none: the device takes several reads at once and waits for more.
        if (position / _clockFrequency < 2.0 * _bufferFrames / Format.SampleRate)
        {
            _silence = behind;
            return false;
        }

        // One silence until the device moves evenly again: what it was given meanwhile it takes up some fills later.
        if (_silent)
        {
            _silent = position == _silentAt || behind - _silence > ClockJitter;
            _silence = behind;
            return false;
        }

        if (behind - _silence < LeastSilence)
        {
            // The two clocks drift apart by parts in a million: followed, or hours would add up to a silence.
            _silence += (behind - _silence) * 0.01;
            return false;
        }

        _silence = behind;
        _silent = true;
        _silentAt = position;
        return true;
    }

    private static void Check(int result, string doing)
    {
        if (result < 0)
        {
            throw new DeviceException(result switch
            {
                AudioClient.DeviceInvalidated => "The audio device is gone.",
                AudioEndpoints.NotFound => "There is no audio output device.",
                _ => $"Could not {doing} (0x{result:X8}).",
            }, Marshal.GetExceptionForHR(result));
        }
    }
}
