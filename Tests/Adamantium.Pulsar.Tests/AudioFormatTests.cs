using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

[TestFixture]
public class AudioFormatTests
{
    [Test]
    public void SpeakersNotSaid_AreTheUsual_AndSaid_AreOneForEachChannel()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AudioFormat.Default.Speakers, Is.EqualTo(Speakers.FrontLeft | Speakers.FrontRight));
            Assert.That(new AudioFormat(48000, 2, Speakers.FrontLeft | Speakers.FrontRight), Is.EqualTo(AudioFormat.Default));
            Assert.That(new AudioFormat(48000, 1).Speakers, Is.EqualTo(Speakers.FrontCenter));
            Assert.That(new AudioFormat(48000, 7).Speakers, Is.EqualTo((Speakers)0x70F));
            Assert.That(new AudioFormat(48000, 9).Speakers, Is.EqualTo(Speakers.None));
            Assert.That(() => new AudioFormat(48000, 2, Speakers.FrontCenter), Throws.ArgumentException);
            Assert.That(() => new AudioFormat(48000, 1, (Speakers)0x40000), Throws.ArgumentException);
            Assert.That(() => new SoundData(new AudioFormat(48000, 9), new float[9]), Throws.ArgumentException);
        });
    }

    [Test]
    public void YearsOfSound_AreCountedToTheFrame()
    {
        var format = new AudioFormat(48000, 2);
        var years = TimeSpan.FromDays(3650);

        var frames = format.FramesIn(years);

        Assert.Multiple(() =>
        {
            Assert.That(frames, Is.EqualTo(3650L * 24 * 3600 * 48000));
            Assert.That(format.DurationOf(frames), Is.EqualTo(years));
        });
    }
}
