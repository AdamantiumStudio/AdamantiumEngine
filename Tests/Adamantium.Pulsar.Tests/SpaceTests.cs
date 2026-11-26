using Adamantium.Mathematics;
using NUnit.Framework;
using Vector3F = Adamantium.Mathematics.Vector3<float>;

namespace Adamantium.Pulsar.Tests;

/// <summary>Voices placed in space: how loud by their distance and facing, where across, and their Doppler shift.</summary>
[TestFixture]
public class SpaceTests
{
    private const int Rate = 48000;
    private const float Middle = 0.70710677f;
    private const float Exact = 1e-5f;
    // The mixer's: positions are told a block at a time.
    private const int Block = 256;
    private const int Blocks = Block * 200;

    private AudioEngine _engine;

    [SetUp]
    public void CreateEngine() => _engine = new AudioEngine();

    [TearDown]
    public void DisposeEngine() => _engine.Dispose();

    [TestCase(AttenuationCurve.Inverse, 1f, 0.5f, 1f)]
    [TestCase(AttenuationCurve.Inverse, 2f, 1f, 0.5f)]
    [TestCase(AttenuationCurve.Inverse, 4f, 1f, 0.25f)]
    [TestCase(AttenuationCurve.Inverse, 2f, 3f, 0.25f)]
    [TestCase(AttenuationCurve.Inverse, 1000f, 1f, 0.1f)]
    [TestCase(AttenuationCurve.Linear, 6f, 1f, 0.5f)]
    [TestCase(AttenuationCurve.Linear, 20f, 1f, 0f)]
    [TestCase(AttenuationCurve.Exponential, 2f, 2f, 0.25f)]
    [TestCase(AttenuationCurve.None, 50f, 1f, 1f)]
    public void AnAttenuation_FallsByItsCurve_BetweenNearAndFar(AttenuationCurve curve, float distance, float rolloff,
        float expected)
    {
        var attenuation = new Attenuation
        {
            Curve = curve, Near = 1, Far = curve == AttenuationCurve.Inverse ? 10 : 11, Rolloff = rolloff,
        };

        Assert.That(attenuation.GainAt(distance), Is.EqualTo(expected).Within(Exact));
    }

    [TestCase(30f, 1f)]
    [TestCase(45f, 1f)]
    [TestCase(67.5f, 0.6f)]
    [TestCase(90f, 0.2f)]
    [TestCase(170f, 0.2f)]
    public void ACone_IsLoudWithin_AndQuietPastIt(float angle, float expected)
    {
        var cone = new SoundCone { InnerAngle = 90, OuterAngle = 180, OuterGain = 0.2f };

        Assert.That(cone.GainAt(angle), Is.EqualTo(expected).Within(Exact));
    }

    [Test]
    public void AVoiceInSpace_IsAsLoudAsItsDistance_AndWhereItIsAcross()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HeardAt(new Vector3F(2, 0, 0)), Is.EqualTo(new[] { 0f, 0.5f }).Within(Exact), "to the right");
            Assert.That(HeardAt(new Vector3F(-4, 0, 0)), Is.EqualTo(new[] { 0.25f, 0f }).Within(Exact), "to the left");
            Assert.That(HeardAt(new Vector3F(0, 0, 2)), Is.EqualTo(new[] { 0.5f * Middle, 0.5f * Middle }).Within(Exact),
                "in front");
            Assert.That(HeardAt(new Vector3F(0, -2, 0)), Is.EqualTo(new[] { 0.5f * Middle, 0.5f * Middle }).Within(Exact),
                "above");
            Assert.That(HeardAt(Vector3F.Zero), Is.EqualTo(new[] { Middle, Middle }).Within(Exact), "where the listener is");
        });
    }

    [Test]
    public void TheListenerTurned_HearsItOnTheOtherSide()
    {
        // Turned to face +X, its right ear is to -Z.
        _engine.SetListener(new Listener { Right = new Vector3F(0, 0, -2) });

        Assert.That(HeardAt(new Vector3F(0, 0, 2)), Is.EqualTo(new[] { 0.5f, 0f }).Within(Exact));
    }

    // Their lengths are taken in double: in float the least go to 0 and the greatest to infinity.
    [Test]
    public void DirectionsOfAnyLength_AreDirections()
    {
        _engine.SetListener(new Listener { Right = new Vector3F(1e-23f, 0, 0) });
        Assert.That(HeardAt(new Vector3F(2, 0, 0)), Is.EqualTo(new[] { 0f, 0.5f }).Within(Exact), "a short right");
        Assert.That(HeardAt(new Vector3F(0, 0, 2)), Is.EqualTo(new[] { 0.5f * Middle, 0.5f * Middle }).Within(Exact),
            "a short right, ahead");

        _engine.SetListener(new Listener { Right = new Vector3F(-1e30f, 0, 0) });
        Assert.That(HeardAt(new Vector3F(2, 0, 0)), Is.EqualTo(new[] { 0.5f, 0f }).Within(Exact), "a long right");

        _engine.SetListener(new Listener());
        var cone = new SoundCone { InnerAngle = 60, OuterAngle = 120, OuterGain = 0.25f };
        foreach (var length in new[] { 1e-23f, 1e30f })
        {
            var away = new VoicePlace { Position = new Vector3F(0, 0, 1), Facing = new Vector3F(0, 0, length) };
            _engine.Play(Constant(10), new VoiceSettings { Spatial = new SpatialSettings { Place = away, Cone = cone } });
            Assert.That(Render(Block)[..2], Is.EqualTo(new[] { 0.25f * Middle, 0.25f * Middle }).Within(Exact),
                $"facing away, {length} long");
        }
    }

    [Test]
    public void ItsOwnPan_IsNotHeard_AndASoundOfTwoIsHeardAsOne()
    {
        _engine.Play(Constant(1), new VoiceSettings { Pan = -1, Spatial = At(new Vector3F(1, 0, 0)) });
        Assert.That(Render(1), Is.EqualTo(new[] { 0f, 1f }).Within(Exact));

        Renew(new AudioEngineSettings());
        var stereo = _engine.AddSound(new SoundData(AudioFormat.Default, [.. Enumerable.Repeat(new[] { 1f, 0f }, 10).SelectMany(x => x)]));
        _engine.Play(stereo, new VoiceSettings { Spatial = At(new Vector3F(0, 0, 1)) });

        Assert.That(Render(1), Is.EqualTo(new[] { 0.5f * Middle, 0.5f * Middle }).Within(Exact));
    }

    [Test]
    public void AVoiceFacingAway_IsHeardByItsCone()
    {
        var cone = new SoundCone { InnerAngle = 60, OuterAngle = 120, OuterGain = 0.25f };
        var away = new VoicePlace { Position = new Vector3F(0, 0, 1), Facing = new Vector3F(0, 0, 3) };
        var toward = away with { Facing = new Vector3F(0, 0, -1) };

        _engine.Play(Constant(1), new VoiceSettings { Spatial = new SpatialSettings { Place = away, Cone = cone } });
        Assert.That(Render(1), Is.EqualTo(new[] { 0.25f * Middle, 0.25f * Middle }).Within(Exact));

        Renew(new AudioEngineSettings());
        _engine.Play(Constant(1), new VoiceSettings { Spatial = new SpatialSettings { Place = toward, Cone = cone } });
        Assert.That(Render(1), Is.EqualTo(new[] { Middle, Middle }).Within(Exact));
    }

    // A tenth of sound's speed toward the listener: 343 / (343 - 34.3) times as fast; the listener as fast toward it:
    // (343 + 34.3) / 343.
    [TestCase(-34.3f, 0f, 1.1111111f)]
    [TestCase(0f, 34.3f, 1.1f)]
    [TestCase(34.3f, 0f, 0.9090909f)]
    [TestCase(0f, 0f, 1f)]
    [TestCase(-34.3f, 34.3f, 1.2222222f)]
    public void AVoiceMovingToward_IsHigher_ByTheDopplerShift(float voiceSpeed, float listenerSpeed, float shift)
    {
        _engine.SetListener(new Listener { Velocity = new Vector3F(0, 0, listenerSpeed) });
        var place = new VoicePlace { Position = new Vector3F(0, 0, 10), Velocity = new Vector3F(0, 0, voiceSpeed) };
        var voice = _engine.Play(Constant(Rate * 2), new VoiceSettings { Spatial = new SpatialSettings { Place = place } });

        Render(Blocks);

        Assert.That(_engine.PositionOf(voice), Is.EqualTo(Blocks * shift).Within(2));
    }

    [Test]
    public void ADopplerScaleOf0_ShiftsNothing()
    {
        var place = new VoicePlace { Position = new Vector3F(0, 0, 10), Velocity = new Vector3F(0, 0, -100) };
        var voice = _engine.Play(Constant(Rate * 2),
            new VoiceSettings { Spatial = new SpatialSettings { Place = place, DopplerScale = 0 } });

        Render(Blocks);

        Assert.That(_engine.PositionOf(voice), Is.EqualTo(Blocks));
    }

    // Followed over 50 ms, not stepped a block at a time.
    [Test]
    public void ADopplerShiftThatChanges_GetsThereSmoothly()
    {
        var place = new VoicePlace { Position = new Vector3F(0, 0, 10) };
        var voice = _engine.Play(Constant(Rate * 2), new VoiceSettings { Spatial = new SpatialSettings { Place = place } });
        Render(Block);

        _engine.SetPlace(voice, place with { Velocity = new Vector3F(0, 0, -34.3f) });
        Render(Block);
        var firstBlock = _engine.PositionOf(voice) - Block;
        Render(Block * 19);
        var before = _engine.PositionOf(voice);
        Render(Block);

        Assert.That(firstBlock, Is.InRange(Block + 1, Block * 1.05f));
        Assert.That(_engine.PositionOf(voice) - before, Is.EqualTo(Block * 1.1111111f).Within(1));
    }

    [Test]
    public void AVoiceMoved_IsNotHeardToJump()
    {
        var voice = _engine.Play(Constant(Rate), new VoiceSettings { Spatial = At(new Vector3F(-1, 0, 0)) });
        var heard = new List<float>();
        for (var block = 0; block < 20; block++)
        {
            _engine.SetPlace(voice, new VoicePlace { Position = new Vector3F(block % 2 == 0 ? 1 : -1, 0, 0) });
            heard.AddRange(Render(256));
        }

        for (var index = 2; index < heard.Count; index++)
        {
            Assert.That(Math.Abs(heard[index] - heard[index - 2]), Is.LessThan(0.01f), $"frame {index / 2}");
        }
    }

    [Test]
    public void OfVoicesInSpace_TheNearAreHeard_TheFarUnheard()
    {
        Renew(new AudioEngineSettings { AudibleVoices = 1 });
        _engine.Play(Constant(Rate), new VoiceSettings { Volume = 1, Spatial = At(new Vector3F(500, 0, 0)) });
        _engine.Play(Constant(Rate), new VoiceSettings { Volume = 0.1f, Spatial = At(new Vector3F(-1, 0, 0)) });

        Assert.That(Render(1), Is.EqualTo(new[] { 0.1f, 0f }).Within(Exact));
    }

    [Test]
    public void WhatCanNotBe_IsRefused()
    {
        var sound = Constant(10);
        Assert.Multiple(() =>
        {
            Assert.That(() => _engine.Play(sound, new VoiceSettings { Spatial = new SpatialSettings { Attenuation = new Attenuation { Near = 0 } } }),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => _engine.Play(sound, new VoiceSettings { Spatial = new SpatialSettings { Attenuation = new Attenuation { Curve = AttenuationCurve.Linear } } }),
                Throws.InstanceOf<ArgumentOutOfRangeException>(), "linear with no end");
            Assert.That(() => _engine.Play(sound, new VoiceSettings { Spatial = new SpatialSettings { Cone = new SoundCone { InnerAngle = 90, OuterAngle = 60 } } }),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => _engine.Play(sound, new VoiceSettings { Spatial = new SpatialSettings { Cone = default } }),
                Throws.InstanceOf<ArgumentOutOfRangeException>(), "a cone of no angle");
            Assert.That(() => _engine.Play(sound, new VoiceSettings { Spatial = At(new Vector3F(float.NaN, 0, 0)) }),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => _engine.Play(sound, new VoiceSettings { Spatial = new SpatialSettings { DopplerScale = -1 } }),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => _engine.SetListener(new Listener { Right = Vector3F.Zero }), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => _engine.SetPlace(default, new VoicePlace { Velocity = new Vector3F(float.PositiveInfinity, 0, 0) }),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new AudioEngine(new AudioEngineSettings { SpeedOfSound = 0 }), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
        Assert.That(_engine.Voices, Is.Zero);
    }

    [Test]
    public void RenderingVoicesInSpace_AllocatesNothing()
    {
        var mono = Constant(Rate);
        var surround = _engine.AddSound(new SoundData(new AudioFormat(Rate, 6), new float[Rate * 6]));
        var voices = new List<VoiceId>();
        for (var index = 0; index < 20; index++)
        {
            var settings = new VoiceSettings { Loops = true, Spatial = At(new Vector3F(index, 0, 1)) };
            voices.Add(_engine.Play(index % 2 == 0 ? mono : surround, settings));
        }

        var samples = new float[960];
        _engine.Render(samples, AudioFormat.Default);
        var least = long.MaxValue;
        for (var round = 0; round < 5; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var count = 0; count < 200; count++)
            {
                _engine.SetPlace(voices[count % voices.Count], new VoicePlace { Position = new Vector3F(count, 1, 2), Velocity = Vector3F.UnitX });
                _engine.Render(samples, AudioFormat.Default);
            }

            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.That(least, Is.Zero);
    }

    private void Renew(AudioEngineSettings settings)
    {
        _engine.Dispose();
        _engine = new AudioEngine(settings);
    }

    private static SpatialSettings At(Vector3F position) => new() { Place = new VoicePlace { Position = position } };

    // Its first frame, of a block that ends it: the next is heard from a block of its own.
    private float[] HeardAt(Vector3F position)
    {
        _engine.Play(Constant(10), new VoiceSettings { Spatial = At(position) });
        return Render(Block)[..2];
    }

    private SoundId Constant(int frames)
    {
        var samples = new float[frames];
        Array.Fill(samples, 1f);
        return _engine.AddSound(new SoundData(new AudioFormat(Rate, 1), samples));
    }

    private float[] Render(int frames)
    {
        var samples = new float[frames * 2];
        _engine.Render(samples, AudioFormat.Default);
        return samples;
    }
}
