using System.Runtime.InteropServices;
using Adamantium.Win32.Audio;

namespace Adamantium.Pulsar.Outputs;

/// <summary>The default output device of Windows, in shared mode, filled each time the device says it has played some.
/// Windows converts to the device's own format.</summary>
internal sealed class WasapiOutput(AudioFormat format, TimeSpan buffer) : ThreadedOutput(format)
{
    private const int WaitFailed = -1;
    private const int DeviceWantsMore = 0;
    // The device's clock moves by whole reads of its own, 10 ms apart as a rule: less than half of one is no silence.
    private const double LeastSilence = 0.005;

    private AudioClient _client;
    private AudioRenderClient _render;
    private AudioClock _clock;
    private int _bufferFrames;
    private double _clockFrequency;
    private double _silence;
    private bool _clockRuns;

    protected override void Play()
    {
        var com = AudioThread.EnterCom();
        var proAudio = AudioThread.EnterProAudio();
        using var wantsMore = new AutoResetEvent(false);
        try
        {
            Open(wantsMore);
            Fill();
            Check(_client.Start(), "start the stream");
            Opened(_bufferFrames);
            PlayUntilStopped(wantsMore);
            _client.Stop();
        }
        finally
        {
            Close();
            AudioThread.LeaveProAudio(proAudio);
            if (com >= 0)
            {
                AudioThread.LeaveCom();
            }
        }
    }

    private void Open(AutoResetEvent wantsMore)
    {
        Check(AudioEndpoints.ActivateDefaultOutput(out _client), "open the default output device");
        var waveFormat = WaveFormatExtensible.Float(Format.SampleRate, Format.Channels);
        Check(_client.Initialize(AudioClientFlags.EventCallback | AudioClientFlags.NoPersist |
            AudioClientFlags.ConvertFormat | AudioClientFlags.ConvertQuality, buffer.Ticks, waveFormat), "open a stream");
        Check(_client.SetEventHandle(wantsMore.SafeWaitHandle.DangerousGetHandle()), "hear the device");
        Check(_client.GetBufferSize(out _bufferFrames), "read the buffer's size");
        Check(_client.GetRenderClient(out _render), "write to the stream");
        Check(_client.GetClock(out _clock), "read the stream's clock");
        Check(_clock.GetFrequency(out var frequency), "read the stream's clock");
        _clockFrequency = frequency;
        _clockRuns = false;
    }

    private void Close()
    {
        _clock.Release();
        _render.Release();
        _client.Release();
        _clock = default;
        _render = default;
        _client = default;
    }

    private void PlayUntilStopped(AutoResetEvent wantsMore)
    {
        var more = wantsMore.SafeWaitHandle.DangerousGetHandle();
        var stopping = Stopping.SafeWaitHandle.DangerousGetHandle();
        while (true)
        {
            var came = AudioThread.WaitForEither(more, stopping);
            if (came == WaitFailed)
            {
                throw new AudioException($"The audio device can not be waited for (error {Marshal.GetLastPInvokeError()}).");
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
        try
        {
            Render(new Span<float>(samples, frames * Format.Channels));
        }
        finally
        {
            _render.ReleaseBuffer(frames);
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
        if (!_clockRuns)
        {
            _clockRuns = true;
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
        return true;
    }

    private static void Check(int result, string doing)
    {
        if (result < 0)
        {
            throw new AudioException(result switch
            {
                AudioClient.DeviceInvalidated => "The audio device is gone.",
                AudioEndpoints.NotFound => "There is no audio output device.",
                _ => $"Could not {doing} (0x{result:X8}).",
            }, Marshal.GetExceptionForHR(result));
        }
    }
}
