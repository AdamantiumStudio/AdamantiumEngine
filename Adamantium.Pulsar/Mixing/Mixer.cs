using System.Numerics;
using System.Runtime.InteropServices;

namespace Adamantium.Pulsar.Mixing;

/// <summary>Voices and buses, as the output's thread has them: changed by commands alone. Allocates nothing once made.
/// </summary>
internal sealed class Mixer
{
    // 5 ms: how finely a gain moves, and how fast one with no time of its own does.
    private const int BlockFrames = 256;
    private const int Channels = 2;
    // -80 dB.
    private const float LeastHeard = 0.0001f;
    // A voice heard already is kept over one as loud.
    private const float KeepHeard = 1.25f;

    private readonly int _sampleRate = AudioFormat.Default.SampleRate;
    private readonly Voice[] _voices;
    private readonly Bus[] _buses;
    private readonly int[] _active;
    private readonly HighestRanked _loudest;
    private readonly SoundStreamer _streamer;
    private readonly int[] _positions;
    // Parents before their children.
    private readonly int[] _busOrder;
    private readonly float[] _read = new float[BlockFrames * Channels];
    private readonly float[] _mixed = new float[BlockFrames * Channels];
    private int _mixedTaken = BlockFrames * Channels;
    private readonly SlotReturns _endedVoices;
    private readonly SlotReturns _removedBuses;
    private int _activeCount;
    private int _busCount;
    private int _heard;
    private long _frame;
    private int _streamGaps;

    public Mixer(AudioEngineSettings settings, SoundStreamer streamer, SlotReturns endedVoices, SlotReturns removedBuses)
    {
        _streamer = streamer;
        _voices = new Voice[settings.Voices];
        _active = new int[settings.Voices];
        _loudest = new HighestRanked(Math.Min(settings.AudibleVoices, settings.Voices));
        _positions = new int[settings.Voices];
        _buses = new Bus[settings.Buses];
        _busOrder = new int[settings.Buses];
        _endedVoices = endedVoices;
        _removedBuses = removedBuses;
        foreach (ref var bus in _buses.AsSpan())
        {
            bus.Samples = new float[BlockFrames * Channels];
        }

        AddBus(0, -1);
    }

    public long Frame => Volatile.Read(ref _frame);

    public int Heard => Volatile.Read(ref _heard);

    public int StreamGaps => Volatile.Read(ref _streamGaps);

    /// <summary>The frame of its sound a voice got to.</summary>
    public int PositionOf(int voice) => Volatile.Read(ref _positions[voice]);

    /// <summary>For one about to be played: the mixer is done with its slot.</summary>
    public void SetPosition(int voice, int frame) => Volatile.Write(ref _positions[voice], frame);

    public void Apply(in Command command)
    {
        switch (command.Kind)
        {
            case CommandKind.Play:
                Play(command);
                break;
            case CommandKind.Stop:
                Stop(ref _voices[command.Slot], Math.Max(1, command.Frames));
                break;
            case CommandKind.SetVoicePaused:
                _voices[command.Slot].Paused = command.Flag;
                break;
            case CommandKind.SetVoiceVolume:
                _voices[command.Slot].Volume.Set(command.Value, command.Frames);
                break;
            case CommandKind.SetVoicePitch:
                _voices[command.Slot].Pitch = command.Value;
                break;
            case CommandKind.SetVoicePan:
                _voices[command.Slot].Pan = command.Value;
                break;
            case CommandKind.AddBus:
                AddBus(command.Slot, command.Bus);
                break;
            case CommandKind.RemoveBus:
                _buses[command.Slot].Removing = true;
                break;
            case CommandKind.SetBusVolume:
                _buses[command.Slot].Volume.Set(command.Value, command.Frames);
                break;
            case CommandKind.SetBusMuted:
                _buses[command.Slot].Muted = command.Flag;
                break;
            case CommandKind.SetBusSolo:
                _buses[command.Slot].Solo = command.Flag;
                break;
            case CommandKind.SetBusPaused:
                _buses[command.Slot].Paused = command.Flag;
                break;
        }
    }

    /// <summary>Whole blocks are mixed, however little is asked for: a gain takes a block to move.</summary>
    public void Render(Span<float> samples)
    {
        while (samples.Length != 0)
        {
            if (_mixedTaken == _mixed.Length)
            {
                MixBlock();
                _mixedTaken = 0;
            }

            var taken = Math.Min(samples.Length, _mixed.Length - _mixedTaken);
            _mixed.AsSpan(_mixedTaken, taken).CopyTo(samples);
            _mixedTaken += taken;
            samples = samples[taken..];
        }
    }

    private void MixBlock()
    {
        ReadBuses();
        SelectHeard();
        var streamsWantMore = false;
        for (var index = 0; index < _activeCount; index++)
        {
            var slot = _active[index];
            ref var voice = ref _voices[slot];
            MixVoice(ref voice);
            Volatile.Write(ref _positions[slot], voice.Stream?.SoundFrame ?? (int)(voice.Position >> 32));
            streamsWantMore |= voice.Stream is { WantsMore: true };
        }

        if (streamsWantMore)
        {
            _streamer.Wake();
        }

        MixBuses();
        ForgetEnded();
        ForgetRemoved();
        Volatile.Write(ref _frame, _frame + BlockFrames);
    }

    // Stopped again, it ends as soon as either says.
    private static void Stop(ref Voice voice, long frames)
    {
        if (!voice.Stopping || frames < voice.Fade.Left)
        {
            voice.Stopping = true;
            voice.Fade.Set(0, frames);
        }
    }

    private void Play(in Command command)
    {
        var settings = command.Settings;
        ref var voice = ref _voices[command.Slot];
        voice = new Voice
        {
            Sound = command.Sound,
            Stream = command.Stream,
            Bus = command.Bus,
            Priority = settings.Priority,
            Loops = settings.Loops,
            Pitch = settings.Pitch,
            Pan = settings.Pan,
            Volume = new Ramp(settings.Volume),
            Fade = new Ramp(0),
            StartFrame = Math.Max(0, settings.StartFrame),
            // A stream begins there itself.
            Position = command.Stream == null ? (long)command.From << 32 : 0,
        };
        voice.Fade.Set(1, command.Frames);
        _active[_activeCount++] = command.Slot;
    }

    private void AddBus(int slot, int parent)
    {
        _buses[slot] = new Bus { Parent = parent, Volume = new Ramp(1), Samples = _buses[slot].Samples };
        _busOrder[_busCount++] = slot;
    }

    private void ReadBuses()
    {
        var anySolo = false;
        for (var index = 0; index < _busCount; index++)
        {
            anySolo |= _buses[_busOrder[index]].Solo;
        }

        for (var index = 0; index < _busCount; index++)
        {
            ref var bus = ref _buses[_busOrder[index]];
            bus.Loudness = bus.Volume.Highest;
            bus.IsPaused = bus.Paused;
            bus.IsSolo = bus.Solo || !anySolo;
            if (bus.Parent >= 0)
            {
                ref var parent = ref _buses[bus.Parent];
                bus.Loudness *= parent.Loudness;
                bus.IsPaused |= parent.IsPaused;
                bus.IsSolo |= parent.IsSolo;
                bus.Removing |= parent.Removing;
            }

            if (bus.Muted || bus.Removing)
            {
                bus.Loudness = 0;
            }

            bus.Volume.Advance(BlockFrames);
        }
    }

    private void SelectHeard()
    {
        _loudest.Clear();
        for (var index = 0; index < _activeCount; index++)
        {
            ref var voice = ref _voices[_active[index]];
            ref var bus = ref _buses[voice.Bus];
            var loudness = voice.Volume.Highest * voice.Fade.Highest * bus.Loudness * (voice.Selected ? KeepHeard : 1);
            voice.Selected = loudness > LeastHeard && bus.IsSolo && !voice.Paused && !bus.IsPaused &&
                (voice.Started || (voice.StartFrame < _frame + BlockFrames && voice.IsReady));
            if (voice.Selected)
            {
                // A positive float's bits are in its order.
                var rank = ((long)voice.Priority << 32) + BitConverter.SingleToInt32Bits(loudness);
                var dropped = _loudest.Add(rank, _active[index]);
                if (dropped >= 0)
                {
                    _voices[dropped].Selected = false;
                }
            }
        }

        Volatile.Write(ref _heard, _loudest.Count);
    }

    private void MixVoice(ref Voice voice)
    {
        var frames = BlockFrames;
        ref var bus = ref _buses[voice.Bus];
        var paused = voice.Paused || bus.IsPaused;
        var offset = 0;
        if (!voice.Started)
        {
            if (voice.Stopping)
            {
                voice.Ended = true;
                return;
            }

            if (paused || voice.StartFrame >= _frame + frames || (!voice.Selected && !voice.IsReady))
            {
                return;
            }

            offset = (int)Math.Max(0, voice.StartFrame - _frame);
            frames -= offset;
            voice.Started = true;
            // Its first sample is heard as it is.
            SetGains(ref voice);
        }

        if (paused && voice.Left == 0 && voice.Right == 0)
        {
            voice.Ended = voice.Stopping;
            return;
        }

        voice.Volume.Advance(frames);
        voice.Fade.Advance(frames);
        var left = voice.Left;
        var right = voice.Right;
        SetGains(ref voice);
        var step = SoundReader.StepOf(voice, _sampleRate);
        if (left == 0 && right == 0 && voice.Left == 0 && voice.Right == 0)
        {
            voice.Ended = !(voice.Stream == null
                ? SoundReader.Skip(ref voice, step, frames)
                : StreamReader.Skip(ref voice, step, frames));
        }
        else
        {
            var channels = voice.Format.Channels;
            var read = _read.AsSpan(0, frames * channels);
            int got;
            if (voice.Stream == null)
            {
                got = SoundReader.Read(ref voice, step, read);
            }
            else
            {
                got = StreamReader.Read(ref voice, step, read, out var starved);
                _streamGaps += starved && !voice.Starved ? 1 : 0;
                voice.Starved = starved;
            }

            PrepareBus(ref bus);
            AddRamped(read, bus.Samples.AsSpan(offset * Channels, got * Channels), channels, left, right,
                (voice.Left - left) / frames, (voice.Right - right) / frames);
            voice.Ended = got < frames;
        }

        voice.Ended |= voice.Stopping && !voice.Fade.IsMoving;
    }

    private static void SetGains(ref Voice voice)
    {
        var gain = voice.Selected ? voice.Volume.Value * voice.Fade.Value : 0;
        if (voice.Format.Channels == 1)
        {
            var angle = (voice.Pan + 1) * (MathF.PI / 4);
            voice.Left = gain * MathF.Cos(angle);
            voice.Right = gain * MathF.Sin(angle);
        }
        else
        {
            voice.Left = gain * Math.Min(1, 1 - voice.Pan);
            voice.Right = gain * Math.Min(1, 1 + voice.Pan);
        }
    }

    private static void PrepareBus(ref Bus bus)
    {
        if (!bus.HasSamples)
        {
            bus.Samples.AsSpan().Clear();
            bus.HasSamples = true;
        }
    }

    private static void AddRamped(ReadOnlySpan<float> source, Span<float> target, int channels, float left, float right,
        float leftStep, float rightStep)
    {
        for (var frame = 0; frame < target.Length / Channels; frame++)
        {
            left += leftStep;
            right += rightStep;
            target[frame * 2] += source[frame * channels] * left;
            target[frame * 2 + 1] += source[frame * channels + channels - 1] * right;
        }
    }

    private void MixBuses()
    {
        var block = _mixed.AsSpan();
        block.Clear();
        for (var index = _busCount - 1; index >= 0; index--)
        {
            ref var bus = ref _buses[_busOrder[index]];
            var gain = bus.Muted || bus.Removing ? 0 : bus.Volume.Value;
            if (!bus.HadSamples)
            {
                bus.Gain = gain;
            }

            if (bus.HasSamples && (gain != 0 || bus.Gain != 0))
            {
                var target = block;
                if (bus.Parent >= 0)
                {
                    ref var parent = ref _buses[bus.Parent];
                    PrepareBus(ref parent);
                    target = parent.Samples;
                }

                AddBlock(bus.Samples, target, bus.Gain, gain);
            }

            bus.HadSamples = bus.HasSamples;
            bus.HasSamples = false;
            bus.Gain = gain;
        }
    }

    private static void AddBlock(ReadOnlySpan<float> source, Span<float> target, float from, float to)
    {
        if (from != to)
        {
            var step = (to - from) * Channels / source.Length;
            AddRamped(source, target, Channels, from, from, step, step);
            return;
        }

        var sourceVectors = MemoryMarshal.Cast<float, Vector<float>>(source);
        var targetVectors = MemoryMarshal.Cast<float, Vector<float>>(target);
        for (var index = 0; index < sourceVectors.Length; index++)
        {
            targetVectors[index] += sourceVectors[index] * to;
        }

        for (var index = sourceVectors.Length * Vector<float>.Count; index < source.Length; index++)
        {
            target[index] += source[index] * to;
        }
    }

    private void ForgetEnded()
    {
        for (var index = _activeCount - 1; index >= 0; index--)
        {
            var slot = _active[index];
            if (_voices[slot].Ended || _buses[_voices[slot].Bus].Removing)
            {
                if (_voices[slot].Stream is { } stream)
                {
                    stream.Release();
                    _streamer.Wake();
                }

                _voices[slot] = default;
                _active[index] = _active[--_activeCount];
                _endedVoices.Add(slot);
            }
        }
    }

    // After a block that ended in silence.
    private void ForgetRemoved()
    {
        var kept = 0;
        for (var index = 0; index < _busCount; index++)
        {
            var slot = _busOrder[index];
            if (_buses[slot].Removing)
            {
                _removedBuses.Add(slot);
            }
            else
            {
                _busOrder[kept++] = slot;
            }
        }

        _busCount = kept;
    }
}
