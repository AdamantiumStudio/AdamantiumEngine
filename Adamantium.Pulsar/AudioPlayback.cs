using Adamantium.Pulsar.Live;
using Adamantium.Pulsar.Playback;

namespace Adamantium.Pulsar;

/// <summary>Plays one sound file at a time, as a player does: open, play, pause, stop, seek, volume; what it is, where it
/// is, its spectrum and waveform to show. The engine, the device and the threads are its own: a player tells it what to
/// do and shows what it says. <see cref="StateChanged"/> and <see cref="Ended"/> are raised on the thread it was made on,
/// where that has a <see cref="SynchronizationContext"/>.</summary>
public sealed class AudioPlayback : IDisposable
{
    // Enough for a player, whose sound is not answered to a key as a game's is; half what a first play waited for.
    private static readonly TimeSpan OutputBuffer = TimeSpan.FromMilliseconds(50);
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
    // Its voices play through it at their full volume: the volume is the bus's, and the spectrum is of what comes into
    // it, at any volume.
    private readonly BusId _bus;
    private ISoundStorage _storage;
    // Of a station: where it is, and what connects to it and reads it, from when it is opened or told to play again.
    private Uri _address;
    private LiveFeed _feed;
    private Sound _sound;
    private SoundId _soundId;
    private VoiceId _voice;
    // Of the sound, the frame it plays from while stopped.
    private long _from;
    private float _volume = 1;
    private AudioPlaybackState _state;
    private bool _disposed;

    /// <summary>To the default device of the system.</summary>
    /// <exception cref="PlatformNotSupportedException">No device output is written for this system yet.</exception>
    public AudioPlayback()
        : this(AudioOutput.CreateDefault(OutputBuffer))
    {
    }

    /// <summary>To <paramref name="output"/>, started at once - the first play is heard as soon as any - and disposed
    /// with it.</summary>
    public AudioPlayback(IAudioOutput output)
        : this(new AudioEngine(), output)
    {
        ArgumentNullException.ThrowIfNull(output);
    }

    /// <summary>Through the application's <paramref name="engine"/>, played by an output of its own, on a bus of its own
    /// under the master.</summary>
    /// <exception cref="InvalidOperationException">The engine has as many buses as it was made for.</exception>
    public AudioPlayback(AudioEngine engine)
        : this(engine ?? throw new ArgumentNullException(nameof(engine)), null)
    {
    }

    private AudioPlayback(AudioEngine engine, IAudioOutput output)
    {
        _engine = engine;
        _output = output;
        _bus = engine.AddBus(BusId.Master);
        engine.KeepPlayed(_bus);
        _context = SynchronizationContext.Current;
        _endCheck = new Timer(_ => Refresh(), null, Timeout.Infinite, Timeout.Infinite);
        // The device is kept until disposed: a play, a pause or a new sound is heard at once, and without a click.
        if (output != null)
        {
            try
            {
                output.Start(engine);
            }
            catch
            {
                _endCheck.Dispose();
                output.Dispose();
                engine.Dispose();
                throw;
            }
        }
    }

    /// <summary>Playing, paused or stopped; raised after a change, and when it ends by itself before <see cref="Ended"/>.
    /// </summary>
    public event EventHandler StateChanged;

    /// <summary>It played to the end, and stopped there: a player plays the next. A station does not end so: one that
    /// stops by itself says why in <see cref="Problem"/>.</summary>
    public event EventHandler Ended;

    /// <summary>A station's stream says another title.</summary>
    public event EventHandler StreamTitleChanged;

    /// <summary>What a station says plays now; null of a file, and until the station says.</summary>
    public string StreamTitle { get; private set; }

    /// <summary>Why a station stopped by itself - it could not be reached, sends what does not play, or did not come
    /// back after a drop; null while nothing went wrong.</summary>
    public string Problem { get; private set; }

    /// <summary>Waited before each connection again after a station drops; null for 1, 2 and 4 s, five in all. For
    /// tests, which do not wait so long.</summary>
    internal IReadOnlyList<TimeSpan> ReconnectWaits { get; set; }

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

                var frame = _voice == default ? _from : Math.Max(0, _engine.PositionOf(_voice));
                return _sound.Format.DurationOf(frame);
            }
        }
    }

    /// <summary>0 is silence, 1 as it is. Set after disposal, it is kept and plays nothing.</summary>
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
                // Disposed, its bus is gone: a slider's last word after the window closed is kept, and no more.
                if (!_disposed)
                {
                    _engine.SetVolume(_bus, value, Fade);
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
        // Said of the sound that plays: the file may be written anew between the two reads.
        var said = SoundInfo.Read(storage, out var length);
        Sound sound = said.Duration > TimeSpan.Zero ? StreamedSound.Open(storage) : SoundFile.Read(storage.Open());
        var info = new SoundInfo(sound.Codec, sound.Format, sound.Duration, SoundInfo.BitrateOf(length, sound.Duration),
            sound.Tags);
        bool ended;
        bool changed;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // Another may have been opened meanwhile.
            ended = EndIfDone();
            changed = Release();
            _storage = storage;
            _sound = sound;
            _soundId = _engine.AddSound(sound);
            _from = 0;
            Info = info;
        }

        RaiseChanged(changed || ended, ended);
    }

    /// <summary>Stops what played, and opens the station at <paramref name="address"/> - an Icecast or SHOUTcast stream
    /// of MP3 over HTTP. It connects at once, in the background: <see cref="Info"/> says what it is once it did, and
    /// <see cref="Play"/> plays it as soon as enough came to play without a gap. A drop is connected again while what came
    /// plays on. <see cref="Stop"/> and <see cref="Close"/> end any wait.</summary>
    /// <exception cref="ArgumentException">It is no http or https address.</exception>
    public void Open(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!address.IsAbsoluteUri || address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("A station is at an http or https address.", nameof(address));
        }

        bool ended;
        bool changed;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ended = EndIfDone();
            changed = Release();
            _address = address;
            Connect();
        }

        RaiseChanged(changed || ended, ended);
    }

    /// <summary>Stops, and lets the sound go.</summary>
    public void Close()
    {
        bool ended;
        bool changed;
        lock (_lock)
        {
            ended = EndIfDone();
            changed = Release();
        }

        RaiseChanged(changed || ended, ended);
    }

    /// <summary>From where it is: from the beginning, or where it was paused or sought to. A station is connected to
    /// again where it was stopped, and plays once enough came (<see cref="AudioPlaybackState.Connecting"/>); paused, it
    /// goes on from where it was paused, as long as what came meanwhile lasts.</summary>
    /// <exception cref="InvalidOperationException">No sound is open, or the engine has no voice to spare.</exception>
    public void Play()
    {
        bool ended;
        bool changed;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sound == null && _address == null)
            {
                throw new InvalidOperationException("No sound is open to play.");
            }

            ended = EndIfDone();
            var before = _state;
            if (_state == AudioPlaybackState.Paused)
            {
                _engine.SetPaused(_voice, false);
                _state = AudioPlaybackState.Playing;
            }
            else if (_state == AudioPlaybackState.Stopped && _address != null)
            {
                if (_feed == null)
                {
                    Connect();
                }

                _state = AudioPlaybackState.Connecting;
                StepLive();
            }
            else if (_state == AudioPlaybackState.Stopped)
            {
                _voice = TryPlayVoice(_from, TimeSpan.Zero);
                if (_voice == default)
                {
                    throw new InvalidOperationException("The engine has no voice to spare.");
                }

                _state = AudioPlaybackState.Playing;
            }

            changed = _state != before;
            _endCheck.Change(EndCheck, EndCheck);
        }

        RaiseChanged(ended, ended);
        RaiseChanged(changed, ended: false);
    }

    public void Pause()
    {
        bool ended;
        bool changed;
        lock (_lock)
        {
            ended = EndIfDone();
            changed = _state is AudioPlaybackState.Playing or AudioPlaybackState.Connecting;
            if (_state == AudioPlaybackState.Playing)
            {
                _engine.SetPaused(_voice, true);
                _state = AudioPlaybackState.Paused;
                _endCheck.Change(Timeout.Infinite, Timeout.Infinite);
            }
            else if (_state == AudioPlaybackState.Connecting)
            {
                // Not played yet: it is not told to play, and what comes is kept for when it is.
                _state = AudioPlaybackState.Stopped;
                _endCheck.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        RaiseChanged(ended, ended);
        RaiseChanged(changed, ended: false);
    }

    /// <summary>Stops, back at the beginning; a station's connection is let go, and made again when it plays.</summary>
    public void Stop()
    {
        bool ended;
        bool changed;
        lock (_lock)
        {
            ended = EndIfDone();
            changed = StopVoice();
            DropFeed();
            _from = 0;
        }

        RaiseChanged(ended, ended);
        RaiseChanged(changed, ended: false);
    }

    /// <summary>To <paramref name="position"/>, kept within the sound; playing or paused it goes on from there.</summary>
    /// <exception cref="InvalidOperationException">The engine has no voice to spare: it is stopped there.</exception>
    public void Seek(TimeSpan position)
    {
        bool ended;
        var failed = false;
        lock (_lock)
        {
            // A station plays as it comes.
            if (_sound == null || _address != null)
            {
                return;
            }

            ended = EndIfDone();
            var frame = Math.Clamp(_sound.Format.FramesIn(position), 0, _sound.Frames);
            if (_state != AudioPlaybackState.Stopped)
            {
                // The new one first: the old plays on where there is none. A paused voice is silent, no fade to hear.
                var paused = _state == AudioPlaybackState.Paused;
                var voice = TryPlayVoice(frame, paused ? TimeSpan.Zero : Fade);
                if (voice == default)
                {
                    StopVoice();
                    failed = true;
                }
                else
                {
                    _engine.Stop(_voice, paused ? TimeSpan.Zero : Fade);
                    _voice = voice;
                    if (paused)
                    {
                        _engine.SetPaused(voice, true);
                    }
                }
            }

            _from = frame;
        }

        RaiseChanged(ended, ended);
        RaiseChanged(failed, ended: false);
        if (failed)
        {
            throw new InvalidOperationException("The engine has no voice to spare.");
        }
    }

    /// <summary>Of the sound playing now, at its own loudness whatever the <see cref="Volume"/>, as classic players show
    /// it: each band 0 (silence) to 1 (full scale), from 40 Hz to 16 kHz spaced as pitch is heard; all 0 while it does
    /// not play. Of this playback's sound only, in an engine that plays others too.</summary>
    public void ReadSpectrum(Span<float> bands)
    {
        lock (_lock)
        {
            if (_state != AudioPlaybackState.Playing)
            {
                bands.Clear();
                return;
            }

            _spectrum.Analyze(_engine, _bus, bands);
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
            ObjectDisposedException.ThrowIf(_disposed, this);
            storage = _storage ?? throw new InvalidOperationException(
                _address != null ? "A station has no waveform: it plays as it comes." : "No sound is open.");
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

            Release();
            _disposed = true;
        }

        _endCheck.Dispose();
        if (_output != null)
        {
            _output.Stop();
            _output.Dispose();
            _engine.Dispose();
        }
        else
        {
            // The application's engine lives on: its bus is given back.
            _engine.RemoveBus(_bus);
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
        catch (Exception)
        {
            // Whatever it was - damaged, taken away, locked - it is drawn as silence, and a drawing waits no longer.
            waveform.Finish();
        }
    }

    // None where the engine has no voice to spare.
    private VoiceId TryPlayVoice(long frame, TimeSpan fadeIn)
    {
        // The time that is that frame again: of 44.1 kHz a frame is not whole ticks, and both ways cut down.
        var rate = _sound.Format.SampleRate;
        var from = TimeSpan.FromTicks((long)(((Int128)frame * TimeSpan.TicksPerSecond + rate - 1) / rate));
        return _engine.Play(_soundId, new VoiceSettings { From = from, FadeIn = fadeIn, Bus = _bus });
    }

    // Stops, and lets the sound go; whether it was playing or paused.
    private bool Release()
    {
        var changed = StopVoice();
        DropFeed();
        _address = null;
        StreamTitle = null;
        Problem = null;
        if (_sound != null)
        {
            _engine.RemoveSound(_soundId);
            _sound = null;
            _storage = null;
        }

        Info = null;
        return changed;
    }

    // Under the lock: a feed of the station, listened to; what it tells of comes on its thread.
    private void Connect()
    {
        Problem = null;
        _feed = new LiveFeed(_address, ReconnectWaits);
        _feed.Began += OnFeedBegan;
        _feed.TitleChanged += OnFeedTitleChanged;
        _feed.Ended += _ => Refresh();
        _feed.Start();
    }

    // Under the lock: the station's connection and its sound let go; what it is, kept to be shown.
    private void DropFeed()
    {
        if (_feed == null)
        {
            return;
        }

        _feed.Dispose();
        _feed = null;
        if (_sound != null)
        {
            _engine.RemoveSound(_soundId);
            _sound = null;
        }
    }

    private void OnFeedBegan(LiveFeed feed)
    {
        lock (_lock)
        {
            if (feed != _feed)
            {
                return;
            }

            _sound = new LiveSound(feed);
            _soundId = _engine.AddSound(_sound);
            Info = new SoundInfo(feed.Codec, feed.Format, TimeSpan.Zero, feed.Bitrate, default)
            {
                IsLive = true,
                Station = feed.Station,
            };
        }

        Raise(StateChanged);
    }

    private void OnFeedTitleChanged(LiveFeed feed, string title)
    {
        lock (_lock)
        {
            if (feed != _feed)
            {
                return;
            }

            StreamTitle = title;
        }

        Raise(StreamTitleChanged);
    }

    // Under the lock: of a station, plays once enough came, and goes on past six hours with a voice anew; one that will
    // not come back stops once what came was played, and says why. Whether its state changed.
    private bool StepLive()
    {
        if (_feed == null)
        {
            return false;
        }

        if (_state == AudioPlaybackState.Connecting)
        {
            if (_sound != null && _feed.IsReady)
            {
                return BeginLiveVoice();
            }

            if (_feed.HasEnded)
            {
                StopLive(_feed.Problem);
                return true;
            }

            return false;
        }

        if (_state != AudioPlaybackState.Playing || _engine.IsPlaying(_voice))
        {
            return false;
        }

        if (_feed.HasEnded)
        {
            StopLive(_feed.Problem);
            return true;
        }

        return !BeginLiveVoice();
    }

    // Under the lock: whether a voice of the station began.
    private bool BeginLiveVoice()
    {
        _voice = TryPlayVoice(0, TimeSpan.Zero);
        if (_voice == default)
        {
            StopLive("The engine has no voice to spare.");
            return false;
        }

        _state = AudioPlaybackState.Playing;
        return true;
    }

    private void StopLive(string problem)
    {
        _voice = default;
        _state = AudioPlaybackState.Stopped;
        _endCheck.Change(Timeout.Infinite, Timeout.Infinite);
        DropFeed();
        Problem = problem;
    }


    // Whether it was playing or paused.
    private bool StopVoice()
    {
        if (_state == AudioPlaybackState.Stopped)
        {
            return false;
        }

        // Connecting, it has none yet.
        if (_voice != default)
        {
            _engine.Stop(_voice, _state == AudioPlaybackState.Playing ? Fade : TimeSpan.Zero);
        }

        _voice = default;
        _state = AudioPlaybackState.Stopped;
        _endCheck.Change(Timeout.Infinite, Timeout.Infinite);
        return true;
    }

    private void Refresh()
    {
        bool ended;
        bool changed;
        lock (_lock)
        {
            ended = EndIfDone();
            changed = !_disposed && StepLive();
        }

        RaiseChanged(ended || changed, ended);
    }

    // Under the lock: whether the voice ended by itself, at the end of the sound; stopped then, back at the beginning.
    // A station's does not end so.
    private bool EndIfDone()
    {
        if (_disposed || _state != AudioPlaybackState.Playing || _address != null || _engine.IsPlaying(_voice))
        {
            return false;
        }

        _voice = default;
        _state = AudioPlaybackState.Stopped;
        _from = 0;
        _endCheck.Change(Timeout.Infinite, Timeout.Infinite);
        return true;
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
