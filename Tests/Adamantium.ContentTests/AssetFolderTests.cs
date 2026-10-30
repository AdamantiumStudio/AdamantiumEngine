using System;
using System.IO;
using System.Linq;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.ContentTests;

/// <summary>What the asset service lists in a folder, and the thumbnails and paths it gives.</summary>
[TestFixture]
public class AssetFolderTests
{
    private string _folder;

    [SetUp]
    public void SetUp()
    {
        _folder = Path.Combine(Path.GetTempPath(), "adamantium-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_folder, true);
    }

    [Test]
    public void AFolder_ListsEachAssetOnce_ByItsKind()
    {
        Touch("Ship.dae");
        WriteAssetFile("Ship.dae.aemf", null);
        Touch("Hull.png");
        Touch("Mono.ttf");
        Touch("Boom.wav");
        Touch("Notes.txt");
        Touch(".hidden");
        Touch("Rock.obj.aemf.writing");
        Directory.CreateDirectory(Path.Combine(_folder, "Inner"));
        Touch("Inner/Deep.png");

        var assets = CreateService().GetAssets(_folder);

        Assert.That(assets.Select(asset => (asset.Name, asset.Kind)), Is.EqualTo(new[]
        {
            ("Boom.wav", AssetKind.Sound),
            ("Hull.png", AssetKind.Image),
            ("Mono.ttf", AssetKind.Font),
            ("Notes.txt", AssetKind.Other),
            ("Ship.dae", AssetKind.Model)
        }));
        Assert.That(assets.Last().FilePath, Is.EqualTo(Path.Combine(_folder, "Ship.dae.aemf")));
    }

    [Test]
    public void AModel_GivesTheThumbnailAndTheSourceOfItsAssetFile()
    {
        WriteAssetFile("Ship.dae.aemf", CreatePng(16, 8));

        var ship = CreateService().GetAssets(_folder).Single();

        Assert.Multiple(() =>
        {
            var thumbnail = ship.ReadThumbnail();
            Assert.That((thumbnail.Width, thumbnail.Height), Is.EqualTo((16u, 8u)));
            Assert.That(ship.Source.Path, Is.EqualTo("Ship.dae"));
        });
    }

    [Test]
    public void AModelWithNoThumbnailYet_HasNone()
    {
        WriteAssetFile("Ship.dae.aemf", null);

        Assert.That(CreateService().GetAssets(_folder).Single().ReadThumbnail(), Is.Null);
    }

    [Test]
    public void AnImage_IsItsOwnThumbnail_DecodedOnEachAsk()
    {
        File.WriteAllBytes(Path.Combine(_folder, "Wide.png"), CreatePng(1024, 512));

        var wide = CreateService().GetAssets(_folder).Single();
        var thumbnail = wide.ReadThumbnail();

        Assert.Multiple(() =>
        {
            Assert.That((thumbnail.Width, thumbnail.Height, thumbnail.FramesCount), Is.EqualTo((1024u, 512u, 1u)));
            Assert.That(wide.ReadThumbnail(), Is.Not.SameAs(thumbnail));
        });
    }

    [Test]
    public void AnImageAskedSmall_IsDecodedShrunk()
    {
        File.WriteAllBytes(Path.Combine(_folder, "Wide.png"), CreatePng(1024, 512));

        var thumbnail = CreateService().GetAssets(_folder).Single().ReadThumbnail(256);

        Assert.That((thumbnail.Width, thumbnail.Height), Is.EqualTo((256u, 128u)));
    }

    [Test]
    public void AnAssetsName_OrdersTheList()
    {
        WriteAssetFile("Ship.dae.aemf", null);
        Touch("Ship.dae-old.png");

        Assert.That(CreateService().GetAssets(_folder).Select(asset => asset.Name),
            Is.EqualTo(new[] { "Ship.dae", "Ship.dae-old.png" }));
    }

    [Test]
    public void AFileThatCannotBeRead_HasNoThumbnail()
    {
        File.WriteAllText(Path.Combine(_folder, "Broken.png"), "not a picture");
        File.WriteAllText(Path.Combine(_folder, "Broken.tga"), "no");
        File.WriteAllText(Path.Combine(_folder, "Broken.obj.aemf"), "not an asset file");

        var assets = CreateService().GetAssets(_folder);

        Assert.That(assets.Select(asset => asset.ReadThumbnail()), Is.All.Null);
        Assert.That(assets.Single(asset => asset.Kind == AssetKind.Model).Source, Is.Null);
    }

    [Test]
    public void AnAssetName_ResolvesToTheFileItLoadsFrom()
    {
        WriteAssetFile("Ship.dae.aemf", null);
        var service = CreateService();
        service.Resolvers.Add(new CookedAssetResolver(_folder));
        service.Resolvers.Add(new FileSystemAssetResolver());

        Assert.Multiple(() =>
        {
            Assert.That(service.Resolve("Ship.dae"), Is.EqualTo(Path.Combine(_folder, "Ship.dae.aemf")));
            Assert.That(service.Resolve("Missing.dae"), Is.Null);
            Assert.That(() => CreateService().Resolve("Ship.dae"), Throws.InvalidOperationException);
        });
    }

    private static AssetService CreateService() => new(new AdamantiumDependencyContainer(), new Satellites());

    private void Touch(string name) => File.WriteAllText(Path.Combine(_folder, name), string.Empty);

    private void WriteAssetFile(string name, byte[] thumbnail)
    {
        using var stream = File.Create(Path.Combine(_folder, name));
        new AssetFile
        {
            Thumbnail = thumbnail,
            Source = new AssetSource { Path = name[..^AssetFile.FileExtension.Length], Importer = "Model" },
            Body = [1]
        }.WriteTo(stream);
    }

    private static byte[] CreatePng(uint width, uint height)
    {
        var bitmap = new FrameBitmap(width, height, Format.R8G8B8A8_UNORM, [new byte[width * height * 4]]);
        var stream = new MemoryStream();
        BitmapLoader.Save(bitmap, stream, ImageFileType.Png);
        return stream.ToArray();
    }
}
