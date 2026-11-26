using Adamantium.Pulsar.Playback;

namespace Adamantium.Pulsar;

/// <summary>Plays one sound file at a time, as a player does: open, play, pause, stop, seek, volume; what it is, where it
/// is, its spectrum and waveform to show. The engine, the device and the threads are its own: a player tells it what to
/// do and shows what it says. <see cref="StateChanged"/> and <see cref="Ended"/> are raised on the thread it was made on,
/// where that has a <see cref="SynchronizationContext"/>.</summary>
public sealed class AudioPlayback : IDisposable
{
    private static readonly TimeSpan OutputBuffer = TimeSpan.FromMilliseconds(100);
    // Of a seek: the old place fades out, the new one in, without a click.
    private static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan EndCheck = TimeSpan.FromMilliseconds(50);

    private readonly Lock _lock = new();
    private readonly AudioEngine _engine;
    // Null for an engine of the application's, played by its own output.
    private readonly IAudioOutput _output;
    private readonly SynchronizationContext _context;
    private readonly SpectrumAnalyzer _spectrum = new();
    private readonly Timer _endCheck;
    private ISoundStorage _storage;
    private Sound _sound;
    private SoundId _soundId;
    private VoiceId _voice;
    // Of the sound, the frame it plays from while stopped.
    private long _from;
    private float _volume = 1;
    private bool _isOutputStarted;
    private AudioPlaybackState _state;
    private bool _disposed;

    /// <summary>To the default device of the system.</summary>
    /// <exception cref="PlatformNotSupportedException">No device output is written for this system yet.</exception>
    public AudioPlayback()
        : this(AudioOutput.CreateDefault(OutputBuffer))
    {
    }

    /// <summary>To <paramref name="output"/>, started once it first plays, and disposed with it.</summary>
    public AudioPlayback(IAudioOutput output)
        : this(new AudioEngine(), output)
    {
        ArgumentNullException.ThrowIfNull(output);
    }

    /// <summary>Through the application's <paramref name="engine"/>, played by an output of its own; its spectrum is then
    /// of all the engine plays.</summary>
    public AudioPlayback(AudioEngine engine)
        : this(engine, null)
    {
        ArgumentNullException.ThrowIfNull(engine);
    }

    private AudioPlayback(AudioEngine engine, IAudioOutput output)
    {
        _engine = engine;
        _output = output;
        _context = SynchronizationContext.Current;
        _endCheck = new Timer(_ => Refresh(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Playing, paused or stopped; raised after a change, and when it ends by itself before <see cref="Ended"/>.
    /// </summary>
    public event EventHandler StateChanged;

    /// <summary>It played to the end, and stopped there: a player plays the next.</summary>
    public event EventHandler Ended;

    public AudioPlaybackState State
    {
        get
        {
            Refresh();
            lock (_lock)
            {
                return _state;
            }
        }
    }

    /// <summary>Of the sound open; null while none is.</summary>
    public SoundInfo Info { get; private set; }

    public TimeSpan Duration => Info?.Duration ?? TimeSpan.Zero;

    /// <summary>Where in the sound it is; where it plays from while stopped.</summary>
    public TimeSpan Position
    {
        get
        {
            Refresh();
            lock (_lock)
            {
                if (_sound == null)
                {
                    return TimeSpan.Zero;
                }

                var frame = _state == AudioPlaybackState.Stopped ? _from : Math.Max(0, _engine.PositionOf(_voice));
                return _sound.Format.DurationOf(frame);
            }
        }
    }

    /// <summary>0 is silence, 1 as it is.</summary>
    public float Volume
    {
        get => _volume;
        set
        {
            if (!(value >= 0 && float.IsFinite(value)))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A volume is 0 or more.");
            }

            lock (_lock)
            {
                _volume = value;
                if (_state != AudioPlaybackState.Stopped)
                {
                    _engine.SetVolume(_voice, value, Fade);
                }
            }
        }
    }

    /// <inheritdoc cref="Open(ISoundStorage)"/>
    public void Open(string path) => Open(new SoundFileStorage(path));

    /// <summary>Stops what played, and opens <paramref name="storage"/>'s sound, stopped at its beginning: streamed, or
    /// whole where it does not say how long it is.</summary>
    /// <exception cref="InvalidDataException">It is no sound file, or one that can not be read; what played is closed.
    /// </exception>
    public void Open(ISoundStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        Close();
        var stream = storage.Open();
        var length = stream.Length - stream.Position;
        long frames;
        using (var decoder = SoundFile.Open(stream))
        {
            frames = decoder.Frames;
        }

        Sound sound = frames > 0 ? StreamedSound.Open(storage) : SoundFile.Read(storage.Open());
        var seconds = sound.Duration.TotalSeconds;
        var info = new SoundInfo(sound.Codec, sound.Format, sound.Duration, (int)Math.Round(length * 8 / seconds), sound.Tags);
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _storage = storage;
            _sound = sound;
            _soundId = _engine.AddSound(sound);
            _from = 0;
            Info = info;
        }
    }

    /// <summary>Stops, and lets the sound go.</summary>
    public void Close()
    {
        bool changed;
        lock (_lock)
        {
            changed = StopVoice();
            if (_sound != null)
            {
                _engine.RemoveSound(_soundId);
                _sound = null;
                _storage = null;
                Info = null;
            }
        }

        RaiseChanged(changed, ended: false);
    }

    /// <summary>From where it is: from the beginning, or where it was paused or sought to.</summary>
    /// <exception cref="InvalidOperationException">No sound is open, or the engine has no voice to spare.</exception>
    public void Play()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sound == null)
            {
                throw new InvalidOperationException("No sound is open to play.");
            }

            switch (_state)
            {
                case AudioPlaybackState.Playing:
                    return;
                case AudioPlaybackState.Paused:
                    _engine.SetPaused(_voice, false);
                    break;
                default:
                    StartOutput();
                    _voice = PlayVoice(_from, TimeSpan.Zero);
                    break;
            }

            _state = AudioPlaybackState.Playing;
            _endCheck.Change(EndCheck, EndCheck);
        }

        RaiseChanged(changed: true, ended: false);
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (_state != AudioPlaybackState.Playing)
            {
                return;
            }

            _engine.SetPaused(_voice, true);
            _state = AudioPlaybackState.Paused;
            _endCheck.Change(Timeout.Infinite, Timeout.Infinite);
        }

        RaiseChanged(changed: true, ended: false);
    }

    /// <summary>Stops, back at the beginning.</summary>
    public void Stop()
    {
        bool changed;
        lock (_lock)
        {
            changed = StopVoice();
            _from = 0;
        }

        RaiseChanged(changed, ended: false);
    }

    /// <summary>To <paramref name="position"/>, kept within the sound; playing or paused it goes on from there.</summary>
    public void Seek(TimeSpan position)
    {
        lock (_lock)
        {
            if (_sound == null)
            {
                return;
            }

            var frame = Math.Clamp(_sound.Format.FramesIn(position), 0, _sound.Frames);
            _from = frame;
            if (_state == AudioPlaybackState.Stopped)
            {
                return;
            }

            // A paused voice is silent: no fade to hear, and none would go on.
            var paused = _state == AudioPlaybackState.Paused;
            _engine.Stop(_voice, paused ? TimeSpan.Zero : Fade);
            _voice = PlayVoice(frame, paused ? TimeSpan.Zero : Fade);
            if (paused)
            {
                _engine.SetPaused(_voice, true);
            }
        }
    }

    /// <summary>Of what is heard now, each band 0 (silence) to 1 (full scale), from 40 Hz to 16 kHz spaced as pitch is
    /// heard; all 0 while it does not play.</summary>
    public void ReadSpectrum(Span<float> bands)
    {
        lock (_lock)
        {
            if (_state != AudioPlaybackState.Playing)
            {
                bands.Clear();
                return;
            }

            _spectrum.Analyze(_engine, bands);
        }
    }

    /// <summary>The waveform of the sound open, of <paramref name="parts"/> parts, read in the background: drawn as its
    /// parts are read. One that can not be read on is silence from there.</summary>
    /// <exception cref="InvalidOperationException">No sound is open.</exception>
    public SoundWaveform ReadWaveform(int parts, CancellationToken cancellation)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(parts, 1);
        ISoundStorage storage;
        SoundWaveform waveform;
        lock (_lock)
        {
            storage = _storage ?? throw new InvalidOperationException("No sound is open.");
            waveform = new SoundWaveform(_sound.Format.Channels, parts, _sound.Frames);
        }

        Task.Run(() => ReadWaveform(storage, waveform, cancellation), cancellation);
        return waveform;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            StopVoice();
            if (_sound != null)
            {
                _engine.RemoveSound(_soundId);
                _sound = null;
            }

            _disposed = true;
        }

        _endCheck.Dispose();
        if (_output != null)
        {
            _output.Stop();
            _output.Dispose();
            _engine.Dispose();
        }
    }

    private static void ReadWaveform(ISoundStorage storage, SoundWaveform waveform, CancellationToken cancellation)
    {
        try
        {
            using var decoder = SoundFile.Open(storage.Open());
            while (!cancellation.IsCancellationRequested && waveform.ReadOn(decoder, 1 << 16))
            {
            }
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            waveform.Finish();
        }
    }

    private VoiceId PlayVoice(long frame, TimeSpan fadeIn)
    {
        var voice = _engine.Play(_soundId, new VoiceSettings
        {
            From = _sound.Format.DurationOf(frame), FadeIn = fadeIn, Volume = _volume,
        });
        return voice != default ? voice : throw new InvalidOperationException("The engine has no voice to spare.");
    }

    private void StartOutput()
    {
        // The device is kept until disposed: a pause or a new sound goes on at once, and without a click.
        if (_output != null && !_isOutputStarted)
        {
            _output.Start(_engine);
            _isOutputStarted = true;
        }
    }

    // Whether it was playing or paused.
    private bool StopVoice()
    {
        if (_state == AudioPlaybackState.Stopped)
        {
            return false;
        }

        _engine.Stop(_voice, _state == AudioPlaybackState.Playing ? Fade : TimeSpan.Zero);
        _voice = default;
        _state = AudioPlaybackState.Stopped;
        _endCheck.Change(Timeout.Infinite, Timeout.Infinite);
        return true;
    }

    // Whether the voice ended by itself, at the end of the sound.
    private void Refresh()
    {
        lock (_lock)
        {
            if (_disposed || _state != AudioPlaybackState.Playing || _engine.IsPlaying(_voice))
            {
                return;
            }

            _voice = default;
            _state = AudioPlaybackState.Stopped;
            _from = 0;
            _endCheck.Change(Timeout.Infinite, Timeout.Infinite);
        }

        RaiseChanged(changed: true, ended: true);
    }

    private void RaiseChanged(bool changed, bool ended)
    {
        if (changed)
        {
            Raise(StateChanged);
        }

        if (ended)
        {
            Raise(Ended);
        }
    }

    private void Raise(EventHandler handler)
    {
        if (handler == null)
        {
            return;
        }

        if (_context != null)
        {
            _context.Post(_ => handler(this, EventArgs.Empty), null);
        }
        else
        {
            handler(this, EventArgs.Empty);
        }
    }
}
