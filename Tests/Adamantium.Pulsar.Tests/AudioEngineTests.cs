using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

/// <summary>What the engine renders, sample by sample, with no device.</summary>
[TestFixture]
public class AudioEngineTests
{
    private const int Rate = 48000;
    // The gain a sound of one channel has in each side at the middle.
    private const float Middle = 0.70710677f;
    private const float Exact = 1e-5f;

    private AudioEngine _engine;

    [SetUp]
    public void CreateEngine() => _engine = new AudioEngine();

    [Test]
    public void NothingPlaying_IsSilence()
    {
        var rendered = new float[2000];
        Array.Fill(rendered, 5f);

        _engine.Render(rendered, AudioFormat.Default);

        Assert.That(rendered, Is.All.Zero);
    }

    [Test]
    public void ASoundAtTheMixersRate_IsHeardAsItIs_ThenEnds()
    {
        var samples = Noise(1000);
        var voice = _engine.Play(Add(samples));

        var rendered = Render(1500);

        for (var frame = 0; frame < 1000; frame++)
        {
            Assert.That(rendered[frame * 2], Is.EqualTo(samples[frame] * Middle).Within(Exact), $"frame {frame}");
            Assert.That(rendered[frame * 2 + 1], Is.EqualTo(samples[frame] * Middle).Within(Exact), $"frame {frame}");
        }

        Assert.That(rendered[2000..], Is.All.Zero);
        Assert.That(_engine.IsPlaying(voice), Is.False);
        Assert.That(_engine.Voices, Is.Zero);
    }

    [Test]
    public void AStereoSound_KeepsItsSides_AndAPanTurnsTheOtherDown()
    {
        var sound = _engine.AddSound(new SoundData(AudioFormat.Default, [.. Enumerable.Repeat(new[] { 0.5f, -0.25f }, 600).SelectMany(x => x)]));
        _engine.Play(sound);
        Assert.That(Render(100)[..4], Is.EqualTo(new[] { 0.5f, -0.25f, 0.5f, -0.25f }).Within(Exact));

        _engine = new AudioEngine();
        sound = _engine.AddSound(new SoundData(AudioFormat.Default, [0.5f, -0.25f]));
        _engine.Play(sound, new VoiceSettings { Pan = 0.5f });

        Assert.That(Render(1), Is.EqualTo(new[] { 0.25f, -0.25f }).Within(Exact));
    }

    [TestCase(-1f, 1f, 0f)]
    [TestCase(0f, Middle, Middle)]
    [TestCase(1f, 0f, 1f)]
    public void AMonoSound_IsPannedKeepingItsPower(float pan, float left, float right)
    {
        _engine.Play(Add(Constant(10)), new VoiceSettings { Pan = pan });

        Assert.That(Render(1), Is.EqualTo(new[] { left, right }).Within(Exact));
    }

    [Test]
    public void VoicesAndBuses_AreAdded()
    {
        var sound = Add(Constant(4000));
        var bus = _engine.AddBus(BusId.Master);
        var inner = _engine.AddBus(bus);
        _engine.SetVolume(bus, 0.5f);
        _engine.SetVolume(inner, 0.5f);
        _engine.Play(sound);
        _engine.Play(sound, new VoiceSettings { Bus = bus, Volume = 0.5f });
        _engine.Play(sound, new VoiceSettings { Bus = inner });

        Assert.That(Last(Render(1000)), Is.EqualTo((1 + 0.25f + 0.25f) * Middle).Within(Exact));
    }

    [TestCase(0.5f)]
    [TestCase(2f)]
    [TestCase(1.5f)]
    public void APitch_PlaysASineOfThatPitch(float pitch)
    {
        var voice = _engine.Play(Add(Sine(440, Rate)), new VoiceSettings { Pitch = pitch, Pan = -1 });

        var rendered = Render(4800);

        for (var frame = 10; frame < 4800; frame++)
        {
            Assert.That(rendered[frame * 2], Is.EqualTo(Math.Sin(Math.Tau * 440 * pitch * frame / Rate)).Within(0.001), $"frame {frame}");
        }

        Assert.That(_engine.PositionOf(voice), Is.EqualTo(4800 * pitch).Within(1));
    }

    [Test]
    public void ASoundOfAnotherRate_KeepsItsPitchAndLength()
    {
        var samples = new float[22050];
        for (var frame = 0; frame < samples.Length; frame++)
        {
            samples[frame] = (float)Math.Sin(Math.Tau * 440 * frame / 22050);
        }

        var sound = _engine.AddSound(new SoundData(new AudioFormat(22050, 1), samples));
        var voice = _engine.Play(sound, new VoiceSettings { Pan = -1 });

        var rendered = Render(47990);

        Assert.That(_engine.IsPlaying(voice), Is.True);
        for (var frame = 10; frame < 4800; frame++)
        {
            Assert.That(rendered[frame * 2], Is.EqualTo(Math.Sin(Math.Tau * 440 * frame / Rate)).Within(0.005), $"frame {frame}");
        }

        Render(20);
        Assert.That(_engine.IsPlaying(voice), Is.False);
    }

    [TestCase(1f)]
    [TestCase(1.37f)]
    public void ALoop_GoesRoundWithoutASeam(float pitch)
    {
        // A lead-in, then ten periods of 100 frames.
        var samples = new float[1200];
        for (var frame = 0; frame < samples.Length; frame++)
        {
            samples[frame] = (float)Math.Sin(Math.Tau * frame / 100);
        }

        var sound = _engine.AddSound(new SoundData(new AudioFormat(Rate, 1), samples, new SoundLoop(100, 1100)));
        _engine.Play(sound, new VoiceSettings { Loops = true, Pitch = pitch, Pan = -1 });

        var rendered = Render(9000);

        for (var frame = 10; frame < 9000; frame++)
        {
            Assert.That(rendered[frame * 2], Is.EqualTo(Math.Sin(Math.Tau * pitch * frame / 100)).Within(0.005), $"frame {frame}");
        }
    }

    [Test]
    public void ALoopShorterThanAStep_PlaysOn()
    {
        var sound = _engine.AddSound(new SoundData(new AudioFormat(Rate, 1), [0.5f, 0.5f, 0.5f], new SoundLoop(1, 2)));
        var voice = _engine.Play(sound, new VoiceSettings { Loops = true, Pitch = 100, Pan = -1 });

        Assert.That(Last(Render(1000)), Is.EqualTo(0.5f).Within(Exact));
        Assert.That(_engine.IsPlaying(voice), Is.True);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(255)]
    [TestCase(256)]
    [TestCase(1000)]
    public void AVoice_BeginsAtItsFrame(int start)
    {
        _engine.Play(Add(Constant(5000)), new VoiceSettings { StartFrame = start, Pan = -1 });

        var rendered = Render(2000);

        for (var frame = 0; frame < 2000; frame++)
        {
            Assert.That(rendered[frame * 2], Is.EqualTo(frame < start ? 0 : 1).Within(Exact), $"frame {frame}");
        }
    }

    [Test]
    public void AVoice_BeginsWhereInItsSoundItIsTold()
    {
        var samples = Noise(Rate);
        _engine.Play(Add(samples), new VoiceSettings { From = TimeSpan.FromSeconds(0.5), Pan = -1 });

        Assert.That(Render(10)[0], Is.EqualTo(samples[Rate / 2]).Within(Exact));
    }

    [Test]
    public void AVoiceBeginningPastItsEnd_EndsAtOnce()
    {
        var voice = _engine.Play(Add(Constant(100)), new VoiceSettings { From = TimeSpan.FromDays(20000) });

        Assert.That(Render(10), Is.All.Zero);
        Assert.That(_engine.IsPlaying(voice), Is.False);
    }

    [Test]
    public void AFadeIn_GoesUpEvenly()
    {
        _engine.Play(Add(Constant(Rate)), new VoiceSettings { FadeIn = TimeSpan.FromSeconds(0.1), Pan = -1 });

        var rendered = Render(Rate / 5);

        Assert.That(rendered[0], Is.LessThan(0.001f));
        Assert.That(rendered[2400 * 2], Is.EqualTo(0.5f).Within(0.01f));
        Assert.That(Last(rendered), Is.EqualTo(1).Within(Exact));
        AssertSmooth(rendered, 0.001f);
    }

    [Test]
    public void AStopWithAFade_GoesDownEvenly_AndEnds()
    {
        var voice = _engine.Play(Add(Constant(Rate)), new VoiceSettings { Pan = -1 });
        Render(1000);

        _engine.Stop(voice, TimeSpan.FromSeconds(0.1));
        var rendered = Render(4700);
        Assert.That(rendered[2400 * 2], Is.EqualTo(0.5f).Within(0.01f));
        Assert.That(_engine.IsPlaying(voice), Is.True);
        AssertSmooth(rendered, 0.001f);

        Render(500);
        Assert.That(_engine.IsPlaying(voice), Is.False);
    }

    // No change is a step: a step is a click.
    [Test]
    public void StopsPausesAndChanges_DoNotClick()
    {
        var voice = _engine.Play(Add(Constant(Rate * 2)), new VoiceSettings { Pan = -1 });
        var bus = _engine.AddBus(BusId.Master);
        var other = _engine.Play(Add(Constant(Rate * 2)), new VoiceSettings { Pan = -1, Bus = bus });
        var all = new List<float>(Render(512));

        _engine.SetVolume(voice, 0.3f);
        all.AddRange(Render(512));
        _engine.SetPaused(voice, true);
        all.AddRange(Render(512));
        _engine.SetPaused(voice, false);
        all.AddRange(Render(512));
        _engine.SetMuted(bus, true);
        all.AddRange(Render(512));
        _engine.SetMuted(bus, false);
        all.AddRange(Render(512));
        _engine.SetPaused(bus, true);
        all.AddRange(Render(512));
        _engine.SetPaused(bus, false);
        all.AddRange(Render(512));
        _engine.SetSolo(bus, true);
        all.AddRange(Render(512));
        _engine.SetSolo(bus, false);
        all.AddRange(Render(512));
        _engine.SetPan(voice, 0.5f);
        all.AddRange(Render(512));
        _engine.RemoveBus(bus);
        all.AddRange(Render(512));
        _engine.Stop(voice);
        all.AddRange(Render(512));

        AssertSmooth(all.ToArray(), 0.01f);
        Assert.That(all[^1], Is.Zero);
        Assert.That(_engine.IsPlaying(voice) || _engine.IsPlaying(other), Is.False);
    }

    [Test]
    public void AVolumeOverATime_GetsThere()
    {
        var voice = _engine.Play(Add(Constant(Rate)), new VoiceSettings { Pan = -1 });
        _engine.SetVolume(voice, 0, TimeSpan.FromSeconds(0.1));

        Assert.That(Render(4800)[2400 * 2], Is.EqualTo(0.5f).Within(0.01f));
        Assert.That(Render(100), Is.All.Zero);
        Assert.That(_engine.IsPlaying(voice), Is.True, "unheard, it plays on");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Paused_ItWaits_AndGoesOnFromThere(bool byItsBus)
    {
        var bus = _engine.AddBus(BusId.Master);
        var voice = _engine.Play(Add(Constant(Rate)), new VoiceSettings { Bus = bus });
        Render(1024);
        Pause(true);
        Render(1024);
        var position = _engine.PositionOf(voice);

        Assert.That(Render(1024), Is.All.Zero);
        Assert.That(_engine.PositionOf(voice), Is.EqualTo(position));
        Pause(false);
        Render(256);

        Assert.That(_engine.PositionOf(voice), Is.EqualTo(position + 256));

        void Pause(bool paused)
        {
            if (byItsBus)
            {
                _engine.SetPaused(bus, paused);
            }
            else
            {
                _engine.SetPaused(voice, paused);
            }
        }
    }

    [Test]
    public void StoppedWhilePaused_ItEnds()
    {
        var voice = _engine.Play(Add(Constant(Rate)));
        Render(512);
        _engine.SetPaused(voice, true);
        Render(512);

        _engine.Stop(voice);
        Render(512);

        Assert.That(_engine.IsPlaying(voice), Is.False);
    }

    [Test]
    public void StoppedBeforeItBegan_ItIsNotHeard()
    {
        var voice = _engine.Play(Add(Constant(Rate)), new VoiceSettings { StartFrame = 1000 });
        _engine.Stop(voice);

        Assert.That(Render(2000), Is.All.Zero);
        Assert.That(_engine.IsPlaying(voice), Is.False);
    }

    [Test]
    public void OfMoreVoicesThanAreHeard_TheMostImportantThenTheLoudestAre()
    {
        _engine = new AudioEngine(new AudioEngineSettings { AudibleVoices = 2 });
        var sound = Add(Constant(Rate));
        _engine.Play(sound, new VoiceSettings { Volume = 0.9f, Pan = -1 });
        _engine.Play(sound, new VoiceSettings { Volume = 0.5f, Pan = -1 });
        _engine.Play(sound, new VoiceSettings { Volume = 0.1f, Pan = -1, Priority = 1 });

        Assert.That(Last(Render(512)), Is.EqualTo(1).Within(Exact));
        Assert.That(_engine.AudibleVoices, Is.EqualTo(2));
        Assert.That(_engine.Voices, Is.EqualTo(3));
    }

    [Test]
    public void AVoiceUnheard_IsHeardFromWhereItGotTo()
    {
        _engine = new AudioEngine(new AudioEngineSettings { AudibleVoices = 1 });
        var ramp = new float[Rate];
        for (var frame = 0; frame < Rate; frame++)
        {
            ramp[frame] = frame / (float)Rate;
        }

        var loud = _engine.Play(Add(Constant(Rate)), new VoiceSettings { Pan = -1, Priority = 1 });
        _engine.Play(Add(ramp), new VoiceSettings { Pan = -1 });
        Render(5120);
        _engine.Stop(loud);
        Render(512);

        var rendered = Render(256);

        Assert.That(Last(rendered), Is.EqualTo((5120 + 512 + 255) / (float)Rate).Within(Exact));
    }

    [Test]
    public void AVoiceUnheard_Ends()
    {
        var voice = _engine.Play(Add(Constant(1000)), new VoiceSettings { Volume = 0 });
        Render(1100);

        Assert.That(_engine.IsPlaying(voice), Is.False);
    }

    [Test]
    public void Solo_LeavesOnlyWhatPlaysThroughIt()
    {
        var sound = Add(Constant(Rate));
        var music = _engine.AddBus(BusId.Master);
        var drums = _engine.AddBus(music);
        var effects = _engine.AddBus(BusId.Master);
        _engine.Play(sound, new VoiceSettings { Pan = -1, Volume = 0.1f });
        _engine.Play(sound, new VoiceSettings { Pan = -1, Volume = 0.2f, Bus = music });
        _engine.Play(sound, new VoiceSettings { Pan = -1, Volume = 0.4f, Bus = drums });
        _engine.Play(sound, new VoiceSettings { Pan = -1, Volume = 0.8f, Bus = effects });

        _engine.SetSolo(music, true);
        Render(512);
        Assert.That(Last(Render(10)), Is.EqualTo(0.6f).Within(Exact));

        _engine.SetSolo(music, false);
        _engine.SetSolo(drums, true);
        Render(512);
        Assert.That(Last(Render(10)), Is.EqualTo(0.4f).Within(Exact));
    }

    [Test]
    public void ABusRemoved_TakesWhatLeadsToIt_AndItsPlaceIsFreeAgain()
    {
        _engine = new AudioEngine(new AudioEngineSettings { Buses = 3 });
        var sound = Add(Constant(Rate));
        for (var round = 0; round < 5; round++)
        {
            var outer = _engine.AddBus(BusId.Master);
            var inner = _engine.AddBus(outer);
            var voice = _engine.Play(sound, new VoiceSettings { Bus = inner, Pan = -1 });
            Assert.That(() => _engine.AddBus(BusId.Master), Throws.InvalidOperationException);
            Assert.That(Last(Render(512)), Is.EqualTo(1).Within(Exact), $"round {round}");

            _engine.RemoveBus(outer);
            Render(512);

            Assert.That(_engine.IsPlaying(voice), Is.False);
            Assert.That(() => _engine.SetVolume(inner, 1), Throws.ArgumentException);
            Assert.That(() => _engine.Play(sound, new VoiceSettings { Bus = outer }), Throws.ArgumentException);
        }
    }

    [Test]
    public void ABusAfterARemovedParent_AndBeforeIt_IsMixedAfterItsParent()
    {
        var first = _engine.AddBus(BusId.Master);
        var second = _engine.AddBus(BusId.Master);
        _engine.RemoveBus(first);
        Render(512);
        // It takes the place of the first, before its parent's.
        var inner = _engine.AddBus(second);
        _engine.Play(Add(Constant(Rate)), new VoiceSettings { Bus = inner, Pan = -1 });

        Assert.That(Last(Render(512)), Is.EqualTo(1).Within(Exact));
    }

    [Test]
    public void AVoiceEnded_IsNotTheOneInItsPlace()
    {
        _engine = new AudioEngine(new AudioEngineSettings { Voices = 1 });
        var sound = Add(Constant(100));
        var first = _engine.Play(sound);
        Assert.That(_engine.Play(sound), Is.EqualTo(default(VoiceId)), "there is no place for another");
        Render(200);

        var second = _engine.Play(Add(Constant(Rate)));
        _engine.Stop(first);
        _engine.SetVolume(first, 0);
        Render(512);

        Assert.That(_engine.IsPlaying(second), Is.True);
        Assert.That(_engine.PositionOf(first), Is.EqualTo(-1));
        Assert.That(Last(Render(10)), Is.EqualTo(Middle).Within(Exact));
    }

    [Test]
    public void ASoundRemoved_IsPlayedToItsEnd_AndPlayedNoMore()
    {
        var sound = Add(Constant(1000));
        _engine.Play(sound);
        _engine.RemoveSound(sound);

        Assert.That(Last(Render(512)), Is.EqualTo(Middle).Within(Exact));
        Assert.That(() => _engine.Play(sound), Throws.ArgumentException);
        Add(Constant(10));
        Assert.That(() => _engine.Play(sound), Throws.ArgumentException, "another took its place");
    }

    [Test]
    public void WithNoOutputTakingThem_CommandsAreNotLost()
    {
        var voice = _engine.Play(Add(Constant(Rate)), new VoiceSettings { Pan = -1 });
        for (var count = 0; count < 10_000; count++)
        {
            _engine.SetVolume(voice, count / 10_000f);
        }

        _engine.SetVolume(voice, 0.25f);

        Assert.That(Last(Render(512)), Is.EqualTo(0.25f).Within(Exact));
    }

    [Test]
    public void AskedFromManyThreadsWhileItRenders_ItLosesNoVoice()
    {
        _engine = new AudioEngine(new AudioEngineSettings { Voices = 100, AudibleVoices = 8 });
        var sound = Add(Constant(300));
        var threads = new List<Thread>();
        var left = 4;
        for (var index = 0; index < left; index++)
        {
            var thread = new Thread(() =>
            {
                for (var count = 0; count < 20_000; count++)
                {
                    var voice = _engine.Play(sound);
                    _engine.SetVolume(voice, 0.5f);
                    _engine.Stop(voice);
                }

                Interlocked.Decrement(ref left);
            });
            threads.Add(thread);
            thread.Start();
        }

        while (Volatile.Read(ref left) != 0)
        {
            Render(256);
        }

        threads.ForEach(thread => thread.Join());
        Render(1024);

        Assert.That(_engine.Voices, Is.Zero);
    }

    [Test]
    public void Rendering_AllocatesNothing()
    {
        _engine = new AudioEngine(new AudioEngineSettings { AudibleVoices = 8 });
        var sound = Add(Noise(Rate));
        var samples = new float[960];
        for (var index = 0; index < 100; index++)
        {
            _engine.Play(sound, new VoiceSettings { Pitch = 1 + index / 100f, Loops = true });
        }

        _engine.Render(samples, AudioFormat.Default);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var count = 0; count < 200; count++)
        {
            _engine.SetVolume(_engine.Play(sound), 0.5f);
            _engine.Render(samples, AudioFormat.Default);
        }

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }

    [Test]
    public void WhatCanNotBe_IsRefused()
    {
        var sound = Add(Constant(10));
        Assert.Multiple(() =>
        {
            Assert.That(() => _engine.Play(default), Throws.ArgumentException);
            Assert.That(() => _engine.Play(sound, default), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => _engine.Play(sound, new VoiceSettings { Volume = float.NaN }), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => _engine.Play(sound, new VoiceSettings { Pan = 2 }), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => _engine.Play(sound, new VoiceSettings { From = TimeSpan.FromSeconds(-1) }), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => _engine.SetVolume(BusId.Master, -1), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => _engine.RemoveBus(BusId.Master), Throws.ArgumentException);
            Assert.That(() => _engine.Render(new float[10], new AudioFormat(44100, 2)), Throws.ArgumentException);
            Assert.That(() => _engine.Stop(default), Throws.Nothing);
        });
        Assert.That(_engine.Voices, Is.Zero, "what was refused took no place");
    }

    private static float[] Constant(int frames)
    {
        var samples = new float[frames];
        Array.Fill(samples, 1f);
        return samples;
    }

    private static float[] Sine(double frequency, int frames)
    {
        var samples = new float[frames];
        for (var frame = 0; frame < frames; frame++)
        {
            samples[frame] = (float)Math.Sin(Math.Tau * frequency * frame / Rate);
        }

        return samples;
    }

    private static float[] Noise(int frames)
    {
        var random = new Random(1);
        var samples = new float[frames];
        for (var frame = 0; frame < frames; frame++)
        {
            samples[frame] = random.NextSingle() * 2 - 1;
        }

        return samples;
    }

    // The left side's.
    private static float Last(float[] rendered) => rendered[^2];

    private static void AssertSmooth(float[] rendered, float most)
    {
        for (var index = 2; index < rendered.Length; index++)
        {
            Assert.That(Math.Abs(rendered[index] - rendered[index - 2]), Is.LessThan(most), $"frame {index / 2}");
        }
    }

    private SoundId Add(float[] samples) => _engine.AddSound(new SoundData(new AudioFormat(Rate, 1), samples));

    private float[] Render(int frames)
    {
        var samples = new float[frames * 2];
        _engine.Render(samples, AudioFormat.Default);
        return samples;
    }
}
