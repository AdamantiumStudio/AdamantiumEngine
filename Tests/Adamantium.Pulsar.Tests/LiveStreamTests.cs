using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

/// <summary>A station played as it comes, from a server on this computer: what it says of itself, its titles, a drop
/// connected again, one that will not come back, one of what does not play.</summary>
[TestFixture]
public class LiveStreamTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan[] ShortWaits = [TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20)];

    private AudioEngine _engine;
    private AudioPlayback _playback;

    [SetUp]
    public void SetUp()
    {
        _engine = new AudioEngine();
        _playback = new AudioPlayback(_engine) { ReconnectWaits = ShortWaits };
    }

    [TearDown]
    public void TearDown()
    {
        _playback.Dispose();
        _engine.Dispose();
    }

    [Test]
    public void AStation_SaysWhatItIs_AndIsHeard()
    {
        using var station = new StationServer(Mp3("cbr128"));

        _playback.Open(station.Address);
        _playback.Play();
        Assert.That(_playback.State, Is.EqualTo(AudioPlaybackState.Connecting));
        WaitUntil(() => _playback.Info != null);

        var info = _playback.Info;
        Assert.That(info.IsLive, Is.True);
        Assert.That(info.Station, Is.EqualTo("Test Radio"));
        Assert.That(info.Codec, Is.EqualTo("MP3"));
        Assert.That(info.Bitrate, Is.EqualTo(128000));
        Assert.That(info.Format, Is.EqualTo(new AudioFormat(44100, 2)));
        Assert.That(info.Duration, Is.EqualTo(TimeSpan.Zero));
        WaitUntil(() => _playback.State == AudioPlaybackState.Playing);
        Assert.That(Loudest(Render(50)), Is.GreaterThan(0.01f));
        Assert.That(_playback.Position, Is.GreaterThan(TimeSpan.Zero));
    }

    // Its song said by the tags of its chain's link, as Icecast's Ogg stations say it; link after link played on.
    [Test]
    public void AnOggStation_IsHeard_AndSaysItsSong()
    {
        using var station = new StationServer(Resource("tags.tagged.opus")) { ContentType = "audio/ogg", SendsIcyTitles = false };

        _playback.Open(station.Address);
        _playback.Play();
        WaitUntil(() => _playback.State == AudioPlaybackState.Playing);

        Assert.That(_playback.Info.Codec, Is.EqualTo("Opus"));
        Assert.That(_playback.StreamTitle, Does.Contain("Утро в лесу"));
        Assert.That(Loudest(Render(50)), Is.GreaterThan(0.001f));
        Assert.That(_playback.State, Is.EqualTo(AudioPlaybackState.Playing));
    }

    // Told once each time the stream says another.
    [Test]
    public void TheTitles_AreTold()
    {
        using var station = new StationServer(Mp3("cbr128")) { Titles = ["One - First", "Two - Second"] };
        var told = new List<string>();
        _playback.StreamTitleChanged += (_, _) =>
        {
            lock (told)
            {
                told.Add(_playback.StreamTitle);
            }
        };

        _playback.Open(station.Address);
        _playback.Play();
        WaitUntil(() => _playback.StreamTitle == "Two - Second");

        lock (told)
        {
            Assert.That(told, Is.EqualTo(new[] { "One - First", "Two - Second" }));
        }
    }

    // What came plays on while it connects again.
    [Test]
    public void ADrop_IsConnectedAgain_AndItPlaysOn()
    {
        using var station = new StationServer(Mp3("cbr128")) { Drops = 1, DropAfter = 200_000 };

        _playback.Open(station.Address);
        _playback.Play();
        WaitUntil(() => _playback.State == AudioPlaybackState.Playing);
        // Played, what came is taken, and more is read until the drop.
        RenderUntil(() => station.Connections == 2);
        Render(100);

        Assert.That(_playback.State, Is.EqualTo(AudioPlaybackState.Playing));
        Assert.That(_playback.Problem, Is.Null);
        Assert.That(Loudest(Render(20)), Is.GreaterThan(0.01f));
    }

    // Once what came was played; a station does not end as a file does, and a player does not go on to the next.
    [Test]
    public void AStationThatWillNotComeBack_Stops_AndSaysWhy()
    {
        using var station = new StationServer(Mp3("cbr128")) { Drops = 1, DropAfter = 60_000, Answers = 1 };
        var ended = 0;
        _playback.Ended += (_, _) => Interlocked.Increment(ref ended);

        _playback.Open(station.Address);
        _playback.Play();
        WaitUntil(() => _playback.State == AudioPlaybackState.Playing);
        RenderUntil(() => _playback.State == AudioPlaybackState.Stopped);

        Assert.That(_playback.Problem, Is.Not.Null);
        // Tried again - HTTP itself tries a connection closed before it answered more than once.
        Assert.That(station.Connections, Is.GreaterThan(1));
        Assert.That(ended, Is.Zero);
    }

    // Connecting again would send the same: it is not.
    [Test]
    public void AStationOfAac_IsNotPlayed_AndSaysSo()
    {
        using var station = new StationServer(Mp3("cbr128")) { ContentType = "audio/aac" };

        _playback.Open(station.Address);
        _playback.Play();
        WaitUntil(() => _playback.State == AudioPlaybackState.Stopped);

        Assert.That(_playback.Problem, Does.Contain("AAC"));
        Assert.That(_playback.Info, Is.Null);
        Assert.That(station.Connections, Is.EqualTo(1));
    }

    // Opened and not played, it connects: one that can not be reached is said all the same.
    [Test]
    public void NoStationThere_OpenedAndNotPlayed_IsSaid()
    {
        var station = new StationServer(Mp3("cbr128"));
        var address = station.Address;
        station.Dispose();
        var changes = 0;
        _playback.StateChanged += (_, _) => Interlocked.Increment(ref changes);

        _playback.Open(address);
        WaitUntil(() => _playback.Problem != null);

        Assert.That(SpinWait.SpinUntil(() => Volatile.Read(ref changes) > 0, Patience), Is.True);
        Assert.That(_playback.State, Is.EqualTo(AudioPlaybackState.Stopped));
    }

    // One that answers and closes within a second, again and again, does not come back as it should: it is given up.
    [Test]
    public void AStationThatClosesEachConnectionSoon_IsGivenUp()
    {
        using var station = new StationServer(Mp3("cbr128")) { Drops = int.MaxValue, DropAfter = 12_000 };

        _playback.Open(station.Address);
        _playback.Play();
        RenderInTimeUntil(() => _playback.State == AudioPlaybackState.Stopped);

        Assert.That(_playback.Problem, Is.Not.Null);
    }

    // Another opened, the song of the one before is said to be gone.
    [Test]
    public void AnotherOpened_TheTitleIsCleared()
    {
        using var station = new StationServer(Mp3("cbr128"));
        _playback.Open(station.Address);
        _playback.Play();
        WaitUntil(() => _playback.StreamTitle != null);
        var cleared = new ManualResetEventSlim();
        _playback.StreamTitleChanged += (_, _) =>
        {
            if (_playback.StreamTitle == null)
            {
                cleared.Set();
            }
        };

        _playback.Open(Mp3Storage());

        Assert.That(cleared.Wait(Patience), Is.True);
    }

    // What an application does when told is its own: a handler that throws does not end the application.
    [Test]
    public void AHandlerThatThrows_DoesNotEndTheApplication()
    {
        using var station = new StationServer(Mp3("cbr128"));
        _playback.StreamTitleChanged += (_, _) => throw new InvalidOperationException("The application's own.");

        _playback.Open(station.Address);
        _playback.Play();
        WaitUntil(() => _playback.StreamTitle != null);
        Thread.Sleep(500);

        Assert.That(_playback.State, Is.Not.EqualTo(AudioPlaybackState.Stopped).Or.Property(nameof(AudioPlayback.Problem)).Not.Null);
    }

    [Test]
    public void NoStationThere_IsSaid()
    {
        var station = new StationServer(Mp3("cbr128"));
        var address = station.Address;
        station.Dispose();

        _playback.Open(address);
        _playback.Play();
        WaitUntil(() => _playback.State == AudioPlaybackState.Stopped);

        Assert.That(_playback.Problem, Is.Not.Null);
    }

    // Stopped, nothing is held; played again, it connects again.
    [Test]
    public void Stopped_ItLetsGo_AndPlayedConnectsAgain()
    {
        using var station = new StationServer(Mp3("cbr128"));
        _playback.Open(station.Address);
        _playback.Play();
        WaitUntil(() => _playback.State == AudioPlaybackState.Playing);

        _playback.Stop();
        Assert.That(_playback.State, Is.EqualTo(AudioPlaybackState.Stopped));
        Assert.That(_playback.Info.Station, Is.EqualTo("Test Radio"));
        _playback.Play();

        Assert.That(_playback.State, Is.EqualTo(AudioPlaybackState.Connecting));
        WaitUntil(() => _playback.State == AudioPlaybackState.Playing);
        Assert.That(station.Connections, Is.EqualTo(2));
    }

    [Test]
    public void AStation_IsNotSoughtIn_AndHasNoWaveform()
    {
        using var station = new StationServer(Mp3("cbr128"));
        _playback.Open(station.Address);
        _playback.Play();
        WaitUntil(() => _playback.State == AudioPlaybackState.Playing);

        Assert.DoesNotThrow(() => _playback.Seek(TimeSpan.FromSeconds(30)));
        Assert.Throws<InvalidOperationException>(() => _playback.ReadWaveform(10, CancellationToken.None));
    }

    [TestCase("ftp://example.org/stream")]
    [TestCase("stream.mp3")]
    public void AnAddressNotOfHttp_IsNoStation(string address)
    {
        Assert.Throws<ArgumentException>(() => _playback.Open(new Uri(address, UriKind.RelativeOrAbsolute)));
    }

    private static byte[] Mp3(string name) => Resource("mp3." + name + ".mp3");

    private static SoundMemoryStorage Mp3Storage() => new(Mp3("cbr128"));

    private static byte[] Resource(string name)
    {
        using var resource = typeof(LiveStreamTests).Assembly.GetManifestResourceStream(name);
        using var copy = new MemoryStream();
        resource.CopyTo(copy);
        return copy.ToArray();
    }

    private static float Loudest(float[] samples) => samples.Max(Math.Abs);

    // Asked of the playback, which looks at the station each time it is.
    private static void WaitUntil(Func<bool> condition) =>
        Assert.That(SpinWait.SpinUntil(condition, Patience), Is.True);

    // Blocks of 10 ms, all of them.
    private float[] Render(int blocks)
    {
        var all = new float[2 * 480 * blocks];
        for (var count = 0; count < blocks; count++)
        {
            Assert.That(SpinWait.SpinUntil(() => _engine.IsReadAhead, Patience), Is.True);
            _engine.Render(all.AsSpan(count * 960, 960), AudioFormat.Default);
        }

        return all;
    }

    // As a device takes blocks, in time: the station's waits between connections are of the clock.
    private void RenderInTimeUntil(Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < Patience)
        {
            Render(1);
            Thread.Sleep(2);
        }

        Assert.That(condition(), Is.True);
    }

    private void RenderUntil(Func<bool> condition)
    {
        for (var count = 0; count < 3000 && !condition(); count++)
        {
            Render(1);
        }

        Assert.That(condition(), Is.True);
    }
}
