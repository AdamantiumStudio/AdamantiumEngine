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
}
