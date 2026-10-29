using System.Collections.Generic;
using System.IO;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Graphics.Core.Models;
using NUnit.Framework;

namespace Adamantium.ContentTests;

/// <summary>What a cooked asset file keeps, what reading a part of it gives, and which files it refuses.</summary>
[TestFixture]
public class AssetFileTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    [Test]
    public void AWrittenFile_ReadsBackWhole()
    {
        var bytes = Write(CreateFile());

        var file = AssetFile.Read(new MemoryStream(bytes), AssetFileSection.All);

        Assert.Multiple(() =>
        {
            Assert.That(file.Thumbnail, Is.EqualTo(Png));
            Assert.That(file.Source.Path, Is.EqualTo("Ship.dae"));
            Assert.That(file.Source.Importer, Is.EqualTo("Model"));
            Assert.That(file.Source.Parameters, Is.EqualTo(new Dictionary<string, string> { ["Scale"] = "2" }));
            Assert.That(file.Body, Is.EqualTo(new byte[] { 7, 8, 9 }));
        });
    }

    [Test]
    public void ReadingTheThumbnail_LeavesTheRestUnread()
    {
        var file = AssetFile.Read(new MemoryStream(Write(CreateFile())), AssetFileSection.Thumbnail);

        Assert.Multiple(() =>
        {
            Assert.That(file.Thumbnail, Is.EqualTo(Png));
            Assert.That(file.Source, Is.Null);
            Assert.That(file.Body, Is.Null);
        });
    }

    [Test]
    public void TheHeader_IsLaidOutAsTheFormatSays()
    {
        var bytes = Write(new AssetFile { Thumbnail = [1, 2], Body = [3] });
        var reader = new BinaryReader(new MemoryStream(bytes));

        Assert.Multiple(() =>
        {
            Assert.That(reader.ReadBytes(4), Is.EqualTo("AEMF"u8.ToArray()));
            Assert.That(reader.ReadUInt16(), Is.EqualTo(AssetFile.FormatVersion));
            Assert.That(reader.ReadUInt16(), Is.EqualTo(2));
            Assert.That((reader.ReadInt32(), reader.ReadInt64(), reader.ReadInt64()),
                Is.EqualTo(((int)AssetFileSection.Thumbnail, 48L, 2L)));
            Assert.That((reader.ReadInt32(), reader.ReadInt64(), reader.ReadInt64()),
                Is.EqualTo(((int)AssetFileSection.Body, 50L, 1L)));
            Assert.That(bytes[48..], Is.EqualTo(new byte[] { 1, 2, 3 }));
        });
    }

    [Test]
    public void ReadingTheThumbnail_DoesNotTouchTheBody()
    {
        var bytes = Write(new AssetFile { Thumbnail = Png, Body = [1, 2, 3] });
        SetEntry(bytes, 1, AssetFileSection.Body, long.MaxValue, 3);

        Assert.Multiple(() =>
        {
            Assert.That(AssetFile.Read(new MemoryStream(bytes), AssetFileSection.Thumbnail).Thumbnail, Is.EqualTo(Png));
            Assert.Throws<AssetFormatException>(() => AssetFile.Read(new MemoryStream(bytes), AssetFileSection.Body));
        });
    }

    [Test]
    public void AnUnknownSection_IsSkipped()
    {
        var bytes = Write(new AssetFile { Thumbnail = Png, Body = [1, 2, 3] });
        SetEntry(bytes, 1, (AssetFileSection)(-1), long.MaxValue, 3);

        var file = AssetFile.Read(new MemoryStream(bytes), AssetFileSection.All);

        Assert.Multiple(() =>
        {
            Assert.That(file.Thumbnail, Is.EqualTo(Png));
            Assert.That(file.Body, Is.Null);
        });
    }

    [Test]
    public void ASectionGivenTwice_IsRefused()
    {
        var bytes = Write(new AssetFile { Thumbnail = Png, Body = [1, 2, 3] });
        SetEntry(bytes, 1, AssetFileSection.Thumbnail, 52, 3);

        Assert.Throws<AssetFormatException>(() => AssetFile.Read(new MemoryStream(bytes), AssetFileSection.Thumbnail));
    }

    [Test]
    public void ASourceWithoutParameters_ReadsWithNone()
    {
        var bytes = Write(new AssetFile { Source = new AssetSource { Path = "a.obj", Parameters = null } });

        Assert.That(AssetFile.Read(new MemoryStream(bytes), AssetFileSection.Source).Source.Parameters, Is.Empty);
    }

    [Test]
    public void ASectionLeftOut_ReadsAsNull()
    {
        var file = AssetFile.Read(new MemoryStream(Write(new AssetFile { Body = [1] })), AssetFileSection.All);

        Assert.Multiple(() =>
        {
            Assert.That(file.Thumbnail, Is.Null);
            Assert.That(file.Source, Is.Null);
            Assert.That(file.Body, Is.EqualTo(new byte[] { 1 }));
        });
    }

    [Test]
    public void AFileStartingInsideAStream_IsReadFromWhereItStarts()
    {
        var stream = new MemoryStream();
        stream.Write([1, 2, 3]);
        CreateFile().WriteTo(stream);
        stream.Position = 3;

        Assert.That(AssetFile.Read(stream, AssetFileSection.Body).Body, Is.EqualTo(new byte[] { 7, 8, 9 }));
    }

    [Test]
    public void AModelCookedBeforeTheHeader_AsksToBeImportedAgain()
    {
        var old = SceneDataSerializer.Serialize(new SceneData { Name = "old" });

        var exception = Assert.Throws<AssetFormatException>(
            () => AssetFile.Read(new MemoryStream(old), AssetFileSection.Body));

        Assert.That(exception.Message, Does.Contain("import its source again"));
    }

    [Test]
    public void AnotherFormatVersion_AsksToBeImportedAgain()
    {
        var bytes = Write(CreateFile());
        bytes[4] = AssetFile.FormatVersion + 1;

        var exception = Assert.Throws<AssetFormatException>(
            () => AssetFile.Read(new MemoryStream(bytes), AssetFileSection.All));

        Assert.That(exception.Message, Does.Contain($"format version {AssetFile.FormatVersion + 1}"));
    }

    [TestCase(3)]
    [TestCase(12)]
    [TestCase(-1)]
    public void AFileCutShort_IsRefused(int length)
    {
        var bytes = Write(CreateFile());
        var cut = bytes[..(length < 0 ? bytes.Length + length : length)];

        Assert.Throws<AssetFormatException>(() => AssetFile.Read(new MemoryStream(cut), AssetFileSection.All));
    }

    [Test]
    public void AFileRefusedByPath_NamesThePath()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllBytes(path, [1, 2, 3, 4, 5, 6, 7, 8]);
        try
        {
            var exception = Assert.Throws<AssetFormatException>(() => AssetFile.Read(path, AssetFileSection.All));

            Assert.That(exception.Message, Does.StartWith(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static AssetFile CreateFile()
    {
        return new AssetFile
        {
            Thumbnail = Png,
            Source = new AssetSource
            {
                Path = "Ship.dae",
                Importer = "Model",
                Parameters = new Dictionary<string, string> { ["Scale"] = "2" }
            },
            Body = [7, 8, 9]
        };
    }

    private static byte[] Write(AssetFile file)
    {
        var stream = new MemoryStream();
        file.WriteTo(stream);
        return stream.ToArray();
    }

    private static void SetEntry(byte[] bytes, int index, AssetFileSection section, long offset, long length)
    {
        var writer = new BinaryWriter(new MemoryStream(bytes));
        writer.Seek(8 + index * 20, SeekOrigin.Begin);
        writer.Write((int)section);
        writer.Write(offset);
        writer.Write(length);
    }
}
