using Adamantium.Pulsar.Decoding;
using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

/// <summary>A file read a part ahead: what it gives is the file, however it is read.</summary>
[TestFixture]
public class ReopenedFileStreamTests
{
    private string _path;
    private byte[] _file;

    [SetUp]
    public void SetUp()
    {
        _file = new byte[300_000];
        new Random(5).NextBytes(_file);
        _path = Path.Combine(Path.GetTempPath(), $"pulsar-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(_path, _file);
    }

    [TearDown]
    public void TearDown() => File.Delete(_path);

    // Small and large, forward and back, across the part read ahead and the end.
    [Test]
    public void ReadsOfAnyLength_AreOfTheFile()
    {
        var random = new Random(9);
        using var stream = new ReopenedFileStream(_path, 1000, -1);
        for (var read = 0; read < 2000; read++)
        {
            var at = random.Next((int)stream.Length + 10);
            var length = random.Next(4) == 0 ? random.Next(70_000, 120_000) : random.Next(1, 300);
            var buffer = new byte[length];
            stream.Position = at;

            var got = stream.Read(buffer);

            var expected = Math.Max(0, Math.Min(length, (int)stream.Length - at));
            Assert.That(got, Is.EqualTo(expected));
            Assert.That(buffer.AsSpan(0, got).SequenceEqual(_file.AsSpan(1000 + at, got)), Is.True);
            Assert.That(stream.Position, Is.EqualTo(at + got));
        }
    }

    // What was read ahead is of the old one; the next part read is refused.
    [Test]
    public void AFileWrittenAnew_IsReadNoMore()
    {
        using var stream = new ReopenedFileStream(_path, 0, -1);
        stream.ReadExactly(new byte[10]);

        File.WriteAllBytes(_path, new byte[100]);
        var ahead = new byte[10];
        stream.Position = 100;
        stream.ReadExactly(ahead);
        stream.Position = 200_000;

        Assert.That(ahead, Is.EqualTo(_file.AsSpan(100, 10).ToArray()));
        Assert.Throws<IOException>(() => stream.ReadExactly(new byte[10]));
    }
}
