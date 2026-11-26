using Adamantium.Pulsar.Mixing;

namespace Adamantium.Pulsar;

/// <summary>Plays sounds: voices, mixed through a tree of buses. It is heard once an <see cref="IAudioOutput"/> is started
/// with it. What is asked of it, from any thread, is done from the next block it renders on.</summary>
public sealed class AudioEngine : IAudioRenderer, IDisposable
{
    private readonly Lock _lock = new();
    private readonly CommandQueue _commands = new();
    private readonly Mixer _mixer;
    private readonly SlotTable _voices;
    private readonly SlotTable _buses;
    private readonly SlotReturns _endedVoices;
    private readonly SlotReturns _removedBuses;
    private readonly int[] _busParents;
    private readonly SoundStreamer _streamer = new();
    private readonly List<Sound> _sounds = [];
    private readonly List<int> _soundGenerations = [];
    private readonly Stack<int> _freeSounds = [];
    // 1 while the commands are applied or a block is rendered.
    private int _mixing;
    private bool _disposed;

    public AudioEngine()
        : this(new AudioEngineSettings())
    {
    }

    public AudioEngine(AudioEngineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.Voices, 1, nameof(settings));
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.AudibleVoices, 1, nameof(settings));
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.Buses, 1, nameof(settings));
        if (!(settings.SpeedOfSound > 0 && float.IsFinite(settings.SpeedOfSound)))
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.SpeedOfSound, "A speed of sound is more than 0.");
        }

        _voices = new SlotTable(settings.Voices);
        _buses = new SlotTable(settings.Buses);
        _busParents = new int[settings.Buses];
        Array.Fill(_busParents, -1);
        _endedVoices = new SlotReturns(settings.Voices);
        _removedBuses = new SlotReturns(settings.Buses);
        _mixer = new Mixer(settings, _streamer, _endedVoices, _removedBuses);
        // The master, slot 0.
        _buses.TryTake(out _);
    }

    /// <summary>The frames mixed so far, a block at a time: the time <see cref="VoiceSettings.StartFrame"/> is in.</summary>
    public long Frame => _mixer.Frame;

    /// <summary>How many voices were heard in the last block.</summary>
    public int AudibleVoices => _mixer.Heard;

    /// <summary>How many voices there are, heard or not.</summary>
    public int Voices
    {
        get
        {
            lock (_lock)
            {
                TakeReturns();
                return _voices.Count;
            }
        }
    }

    /// <summary>How many times a streamed voice fell silent: its sound was not read soon enough.</summary>
    public int StreamGaps => _mixer.StreamGaps;

    /// <summary>Why a streamed sound was last cut off; null for none yet.</summary>
    public string StreamProblem => _streamer.Problem;

    public SoundId AddSound(Sound sound)
    {
        ArgumentNullException.ThrowIfNull(sound);
        lock (_lock)
        {
            if (!_freeSounds.TryPop(out var slot))
            {
                slot = _sounds.Count;
                _sounds.Add(null);
                _soundGenerations.Add(0);
            }

            _sounds[slot] = sound;
            return new SoundId(slot, ++_soundGenerations[slot]);
        }
    }

    /// <summary>Voices playing it play on.</summary>
    public void RemoveSound(SoundId sound)
    {
        lock (_lock)
        {
            SoundOf(sound);
            _sounds[sound.Slot] = null;
            _freeSounds.Push(sound.Slot);
        }
    }

    public VoiceId Play(SoundId sound) => Play(sound, new VoiceSettings());

    /// <summary>The voice, or none where there are <see cref="AudioEngineSettings.Voices"/> already.</summary>
    public VoiceId Play(SoundId sound, in VoiceSettings settings)
    {
        CheckVolume(settings.Volume);
        CheckPitch(settings.Pitch);
        CheckPan(settings.Pan);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.From, TimeSpan.Zero, nameof(settings));
        settings.Spatial?.Check();
        var fade = FramesIn(settings.FadeIn);
        lock (_lock)
        {
            var data = SoundOf(sound);
            var bus = SlotOf(settings.Bus);
            ObjectDisposedException.ThrowIf(_disposed, this);
            TakeReturns();
            if (!_voices.TryTake(out var slot))
            {
                return default;
            }

            // One that begins past its end ends at once.
            var from = (int)Math.Min(data.Frames, data.Format.FramesIn(settings.From));
            var stream = data is StreamedSound streamed ? new SoundStream(streamed, from, settings.Loops) : null;
            if (stream != null)
            {
                _streamer.Add(stream);
            }

            _mixer.SetPosition(slot, from);
            Post(new Command
            {
                Kind = CommandKind.Play,
                Slot = slot,
                Bus = bus,
                Sound = data as SoundData,
                Stream = stream,
                Settings = settings.Spatial is { } spatial
                    ? settings with { Spatial = spatial with { Place = Directed(spatial.Place) } }
                    : settings,
                Frames = fade,
                From = from,
            });
            return new VoiceId(slot, _voices.GenerationOf(slot));
        }
    }

    public void Stop(VoiceId voice) => Stop(voice, TimeSpan.Zero);

    public void Stop(VoiceId voice, TimeSpan fade) =>
        PostTo(voice, new Command { Kind = CommandKind.Stop, Frames = FramesIn(fade) });

    public void SetPaused(VoiceId voice, bool paused) =>
        PostTo(voice, new Command { Kind = CommandKind.SetVoicePaused, Flag = paused });

    public void SetVolume(VoiceId voice, float volume) => SetVolume(voice, volume, TimeSpan.Zero);

    public void SetVolume(VoiceId voice, float volume, TimeSpan over)
    {
        CheckVolume(volume);
        PostTo(voice, new Command { Kind = CommandKind.SetVoiceVolume, Value = volume, Frames = FramesIn(over) });
    }

    public void SetPitch(VoiceId voice, float pitch)
    {
        CheckPitch(pitch);
        PostTo(voice, new Command { Kind = CommandKind.SetVoicePitch, Value = pitch });
    }

    public void SetPan(VoiceId voice, float pan)
    {
        CheckPan(pan);
        PostTo(voice, new Command { Kind = CommandKind.SetVoicePan, Value = pan });
    }

    /// <summary>Moves a voice played with <see cref="VoiceSettings.Spatial"/>; one played without is not placed.</summary>
    public void SetPlace(VoiceId voice, in VoicePlace place)
    {
        CheckPlace(place);
        PostTo(voice, new Command { Kind = CommandKind.SetVoicePlace, Place = Directed(place) });
    }

    /// <summary>Who hears the voices placed in space; until said, one with +X on the right, still.</summary>
    public void SetListener(in Listener listener)
    {
        if (!IsFinite(listener.Right) || !IsFinite(listener.Velocity) || listener.Right == Vector3F.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(listener), listener, "A listener has a right, and moves finitely.");
        }

        lock (_lock)
        {
            Post(new Command
            {
                Kind = CommandKind.SetListener, Listener = listener with { Right = UnitOf(listener.Right) },
            });
        }
    }

    internal static void CheckPlace(in VoicePlace place)
    {
        if (!IsFinite(place.Position) || !IsFinite(place.Velocity) || !IsFinite(place.Facing))
        {
            throw new ArgumentOutOfRangeException(nameof(place), place, "A place is of finite numbers.");
        }
    }

    /// <summary>Its facing of length 1, or none: the mixer divides by no length.</summary>
    internal static VoicePlace Directed(in VoicePlace place) =>
        place.Facing == Vector3F.Zero ? place : place with { Facing = UnitOf(place.Facing) };

    // In double: in float the shortest square to 0, and the longest to infinity.
    private static Vector3F UnitOf(Vector3F vector)
    {
        var length = Math.Sqrt((double)vector.X * vector.X + (double)vector.Y * vector.Y + (double)vector.Z * vector.Z);
        return new Vector3F((float)(vector.X / length), (float)(vector.Y / length), (float)(vector.Z / length));
    }

    /// <summary>Whether it has not ended: paused, unheard and yet to begin are playing too.</summary>
    public bool IsPlaying(VoiceId voice)
    {
        lock (_lock)
        {
            TakeReturns();
            return _voices.Holds(voice.Slot, voice.Generation);
        }
    }

    /// <summary>The frame of its sound a voice got to; -1 for one that ended.</summary>
    public int PositionOf(VoiceId voice)
    {
        lock (_lock)
        {
            TakeReturns();
            return _voices.Holds(voice.Slot, voice.Generation) ? _mixer.PositionOf(voice.Slot) : -1;
        }
    }

    /// <summary>For tests, which render faster than it is heard: no streamed voice wants more now.</summary>
    internal bool IsReadAhead => _streamer.IsIdle;

    /// <exception cref="InvalidOperationException">There are <see cref="AudioEngineSettings.Buses"/> already.</exception>
    public BusId AddBus(BusId parent)
    {
        lock (_lock)
        {
            var parentSlot = SlotOf(parent);
            TakeReturns();
            if (!_buses.TryTake(out var slot))
            {
                throw new InvalidOperationException("There are as many buses as the engine was made for.");
            }

            _busParents[slot] = parentSlot;
            Post(new Command { Kind = CommandKind.AddBus, Slot = slot, Bus = parentSlot, Value = 1 });
            return new BusId(slot, _buses.GenerationOf(slot));
        }
    }

    /// <summary>Removes it and the buses that lead to it, and stops what plays through them.</summary>
    public void RemoveBus(BusId bus)
    {
        lock (_lock)
        {
            var slot = SlotOf(bus);
            if (slot == 0)
            {
                throw new ArgumentException("The master stays.", nameof(bus));
            }

            for (var other = 0; other < _busParents.Length; other++)
            {
                if (LeadsTo(other, slot))
                {
                    _buses.Retire(other);
                }
            }

            Post(new Command { Kind = CommandKind.RemoveBus, Slot = slot });
        }
    }

    public void SetVolume(BusId bus, float volume) => SetVolume(bus, volume, TimeSpan.Zero);

    public void SetVolume(BusId bus, float volume, TimeSpan over)
    {
        CheckVolume(volume);
        PostTo(bus, new Command { Kind = CommandKind.SetBusVolume, Value = volume, Frames = FramesIn(over) });
    }

    public void SetMuted(BusId bus, bool muted) =>
        PostTo(bus, new Command { Kind = CommandKind.SetBusMuted, Flag = muted });

    /// <summary>While any is, only what plays through those that are is heard.</summary>
    public void SetSolo(BusId bus, bool solo) => PostTo(bus, new Command { Kind = CommandKind.SetBusSolo, Flag = solo });

    /// <summary>What plays through it waits, and goes on from there.</summary>
    public void SetPaused(BusId bus, bool paused) =>
        PostTo(bus, new Command { Kind = CommandKind.SetBusPaused, Flag = paused });

    /// <summary>For the output. <paramref name="format"/> is <see cref="AudioFormat.Default"/>.</summary>
    public void Render(Span<float> samples, AudioFormat format)
    {
        if (format != AudioFormat.Default)
        {
            throw new ArgumentException($"The engine mixes in {AudioFormat.Default}, not in {format}.", nameof(format));
        }

        var wait = new SpinWait();
        while (Interlocked.Exchange(ref _mixing, 1) != 0)
        {
            // Another thread applies the commands of a full queue: microseconds.
            wait.SpinOnce(-1);
        }

        try
        {
            ApplyCommands();
            _mixer.Render(samples);
        }
        finally
        {
            Volatile.Write(ref _mixing, 0);
        }
    }

    /// <summary>Stops reading streamed sounds. An output that still plays it plays out what there is.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
        }

        _streamer.Dispose();
    }

    private static long FramesIn(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        return AudioFormat.Default.FramesIn(duration);
    }

    private static void CheckVolume(float volume)
    {
        if (!(volume >= 0 && float.IsFinite(volume)))
        {
            throw new ArgumentOutOfRangeException(nameof(volume), volume, "A volume is 0 or more.");
        }
    }

    private static void CheckPitch(float pitch)
    {
        if (pitch is not (>= 0.01f and <= 100))
        {
            throw new ArgumentOutOfRangeException(nameof(pitch), pitch, "A pitch is from 0.01 to 100.");
        }
    }

    private static bool IsFinite(Vector3F vector) =>
        float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);

    private static void CheckPan(float pan)
    {
        if (pan is not (>= -1 and <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(pan), pan, "A pan is from -1 to 1.");
        }
    }

    private Sound SoundOf(SoundId sound)
    {
        if ((uint)sound.Slot < (uint)_sounds.Count && _soundGenerations[sound.Slot] == sound.Generation &&
            _sounds[sound.Slot] is { } data)
        {
            return data;
        }

        throw new ArgumentException("The engine has no such sound.", nameof(sound));
    }

    private int SlotOf(BusId bus)
    {
        if (bus == BusId.Master || _buses.Holds(bus.Slot, bus.Generation))
        {
            return bus.Slot;
        }

        throw new ArgumentException("The engine has no such bus.", nameof(bus));
    }

    private bool LeadsTo(int bus, int other)
    {
        while (bus >= 0 && bus != other)
        {
            bus = _busParents[bus];
        }

        return bus == other;
    }

    private void PostTo(VoiceId voice, Command command)
    {
        lock (_lock)
        {
            TakeReturns();
            if (_voices.Holds(voice.Slot, voice.Generation))
            {
                command.Slot = voice.Slot;
                Post(command);
            }
        }
    }

    private void PostTo(BusId bus, Command command)
    {
        lock (_lock)
        {
            command.Slot = SlotOf(bus);
            Post(command);
        }
    }

    private void TakeReturns()
    {
        while (_endedVoices.TryTake(out var voice))
        {
            _voices.Free(voice);
        }

        while (_removedBuses.TryTake(out var bus))
        {
            _busParents[bus] = -1;
            _buses.Free(bus);
        }
    }

    // A full queue means no output takes from it, or not now: its commands are applied here.
    private void Post(in Command command)
    {
        while (!_commands.TryAdd(command))
        {
            if (Interlocked.Exchange(ref _mixing, 1) == 0)
            {
                try
                {
                    ApplyCommands();
                }
                finally
                {
                    Volatile.Write(ref _mixing, 0);
                }
            }
            else
            {
                Thread.Yield();
            }
        }
    }

    private void ApplyCommands()
    {
        while (_commands.TryTake(out var command))
        {
            _mixer.Apply(command);
        }
    }
}
