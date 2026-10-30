using System;
using System.IO;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.Graphics.Core.Assets;
using NUnit.Framework;

namespace Adamantium.ContentTests;

[TestFixture]
public class AssetResolverTests
{
    private string _folder;

    [SetUp]
    public void SetUp()
    {
        _folder = "resolver-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, _folder));
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(Path.Combine(AppContext.BaseDirectory, _folder), true);
    }

    [Test]
    public void ARelativePath_IsTakenFromTheApplicationsFolder()
    {
        var full = Path.Combine(AppContext.BaseDirectory, _folder, "a.png");
        File.WriteAllText(full, "png");

        Assert.That(new FileSystemAssetResolver().Resolve(_folder + "/a.png"), Is.EqualTo(full));
    }

    [Test]
    public void AnAbsolutePath_IsTakenAsItIs()
    {
        var full = Path.Combine(AppContext.BaseDirectory, _folder, "a.png");
        File.WriteAllText(full, "png");

        Assert.That(new FileSystemAssetResolver().Resolve(full), Is.EqualTo(full));
    }

    [Test]
    public void ACookedModel_IsFoundBesideTheApplicationUnderItsSourceName()
    {
        var cooked = Path.Combine(AppContext.BaseDirectory, _folder, "m.dae.aemf");
        File.WriteAllText(cooked, "aemf");

        Assert.That(new CookedAssetResolver().Resolve(_folder + "/m.dae"), Is.EqualTo(cooked));
    }

    [Test]
    public void TheAssetService_FindsAnAssetByItsPathFromTheApplicationsFolder()
    {
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, _folder, "a.png"), "png");
        var assets = new AssetService(new AdamantiumDependencyContainer(), new Satellites());
        assets.Resolvers.Add(new FileSystemAssetResolver());

        Assert.That(assets.Exists(_folder + "/a.png"), Is.True);
    }

    [Test]
    public void AModelNotCooked_IsNotFoundCooked()
    {
        Assert.That(new CookedAssetResolver().Exists(_folder + "/m.dae"), Is.False);
    }

    // An image carried inside a model has no file: it is embedded under a name, and loads and is kept like a file.
    [Test]
    public void AnEmbeddedAsset_IsReadFromItsBytes_AndKept()
    {
        var assets = new AssetService(new AdamantiumDependencyContainer(), new Satellites());
        assets.Resolvers.Add(new FileSystemAssetResolver());
        assets.Readers[typeof(Blob)] = new BlobReader();
        var bytes = new byte[] { 1, 2, 3 };
        var options = new AssetLoadOptions { AllowDuplication = false, IgnoreRootDirectory = true };

        assets.Embed("model.glb#image0", bytes);
        var first = assets.Load<Blob>("model.glb#image0", options);
        var again = assets.Load<Blob>("model.glb#image0", options);

        Assert.Multiple(() =>
        {
            Assert.That(assets.Exists("model.glb#image0"), Is.True);
            Assert.That(first.Bytes, Is.SameAs(bytes));
            Assert.That(again, Is.SameAs(first), "kept");
        });
    }

    private sealed record Blob(byte[] Bytes);

    private sealed class BlobReader : IAssetReader
    {
        public System.Threading.Tasks.Task<object> ReadAssetAsync(IAssetService assets,
            AssetReaderParameters parameters) => System.Threading.Tasks.Task.FromResult<object>(new Blob(parameters.Data));
    }
}
