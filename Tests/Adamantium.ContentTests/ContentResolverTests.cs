using System;
using System.IO;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.Graphics.Core.Content;
using NUnit.Framework;

namespace Adamantium.ContentTests;

[TestFixture]
public class ContentResolverTests
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

        Assert.That(new FileSystemContentResolver().Resolve(_folder + "/a.png"), Is.EqualTo(full));
    }

    [Test]
    public void AnAbsolutePath_IsTakenAsItIs()
    {
        var full = Path.Combine(AppContext.BaseDirectory, _folder, "a.png");
        File.WriteAllText(full, "png");

        Assert.That(new FileSystemContentResolver().Resolve(full), Is.EqualTo(full));
    }

    [Test]
    public void ACookedModel_IsFoundBesideTheApplicationUnderItsSourceName()
    {
        var cooked = Path.Combine(AppContext.BaseDirectory, _folder, "m.dae.aemf");
        File.WriteAllText(cooked, "aemf");

        Assert.That(new CookedContentResolver().Resolve(_folder + "/m.dae"), Is.EqualTo(cooked));
    }

    [Test]
    public void TheContentManager_FindsAnAssetByItsPathFromTheApplicationsFolder()
    {
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, _folder, "a.png"), "png");
        var manager = new ContentManager(new AdamantiumDependencyContainer(), new Satellites());
        manager.Resolvers.Add(new FileSystemContentResolver());

        Assert.That(manager.Exists(_folder + "/a.png"), Is.True);
    }

    [Test]
    public void AModelNotCooked_IsNotFoundCooked()
    {
        Assert.That(new CookedContentResolver().Exists(_folder + "/m.dae"), Is.False);
    }

    // An image carried inside a model has no file: it is embedded under a name, and loads and is kept like a file.
    [Test]
    public void AnEmbeddedAsset_IsReadFromItsBytes_AndKept()
    {
        var manager = new ContentManager(new AdamantiumDependencyContainer(), new Satellites());
        manager.Resolvers.Add(new FileSystemContentResolver());
        manager.Readers[typeof(Blob)] = new BlobReader();
        var bytes = new byte[] { 1, 2, 3 };
        var options = new ContentLoadOptions { AllowDuplication = false, IgnoreRootDirectory = true };

        manager.Embed("model.glb#image0", bytes);
        var first = manager.Load<Blob>("model.glb#image0", options);
        var again = manager.Load<Blob>("model.glb#image0", options);

        Assert.Multiple(() =>
        {
            Assert.That(manager.Exists("model.glb#image0"), Is.True);
            Assert.That(first.Bytes, Is.SameAs(bytes));
            Assert.That(again, Is.SameAs(first), "kept");
        });
    }

    private sealed record Blob(byte[] Bytes);

    private sealed class BlobReader : IContentReader
    {
        public System.Threading.Tasks.Task<object> ReadContentAsync(IContentManager contentManager,
            ContentReaderParameters parameters) => System.Threading.Tasks.Task.FromResult<object>(new Blob(parameters.Data));
    }
}
