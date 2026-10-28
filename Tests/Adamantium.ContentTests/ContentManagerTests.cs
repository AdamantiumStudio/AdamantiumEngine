using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.Graphics.Core.Content;
using NUnit.Framework;

namespace Adamantium.ContentTests;

/// <summary>What the content manager keeps, how often it reads, and what it throws.</summary>
[TestFixture]
public class ContentManagerTests
{
    private static readonly ContentLoadOptions Kept = new() { AllowDuplication = false, IgnoreRootDirectory = true };

    [Test]
    public void AKeptAsset_IsReadOnce_ThoughLoadedAtTheSameTimeFromManyThreads()
    {
        var reader = new CountingReader { Delay = TimeSpan.FromMilliseconds(50) };
        var manager = Manager(reader);

        var loads = Enumerable.Range(0, 8).Select(_ => Task.Run(() => manager.Load<Asset>("a", Kept))).ToArray();
        Task.WaitAll(loads);

        Assert.Multiple(() =>
        {
            Assert.That(reader.Reads, Is.EqualTo(1));
            Assert.That(loads.Select(load => load.Result).Distinct().Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public void WithNoOptions_EachLoadReadsAnew()
    {
        var reader = new CountingReader();
        var manager = Manager(reader);

        var first = manager.Load<Asset>("a");
        var second = manager.Load<Asset>("a");

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(reader.Reads, Is.EqualTo(2));
        });
    }

    [Test]
    public void UnloadingByName_ForgetsAndDisposesTheKeptAsset()
    {
        var manager = Manager(new CountingReader());
        var asset = manager.Load<Asset>("a", Kept);

        var unloaded = manager.Unload<Asset>("a");

        Assert.Multiple(() =>
        {
            Assert.That(unloaded, Is.True);
            Assert.That(asset.IsDisposed, Is.True);
            Assert.That(manager.Load<Asset>("a", Kept), Is.Not.SameAs(asset));
            Assert.That(manager.Unload<Asset>("b"), Is.False);
        });
    }

    [Test]
    public void ADisposedAsset_IsReadAgain()
    {
        var manager = Manager(new CountingReader());
        var asset = manager.Load<Asset>("a", Kept);

        asset.Dispose();

        Assert.That(manager.Load<Asset>("a", Kept), Is.Not.SameAs(asset));
    }

    [Test]
    public void AFailedLoad_IsNotKept()
    {
        var reader = new CountingReader { FailFirst = true };
        var manager = Manager(reader);

        Assert.Throws<InvalidOperationException>(() => manager.Load<Asset>("a", Kept));

        Assert.That(manager.Load<Asset>("a", Kept), Is.Not.Null);
    }

    [Test]
    public void AnAssetBeingRead_IsDisposedOnceRead_WhenUnloaded()
    {
        var reader = new CountingReader { Delay = TimeSpan.FromMilliseconds(100) };
        var manager = Manager(reader);
        var load = manager.LoadAsync<Asset>("a", Kept);

        manager.Unload();
        var asset = load.Result;

        Assert.That(SpinWait.SpinUntil(() => asset.IsDisposed, TimeSpan.FromSeconds(5)), Is.True);
    }

    [Test]
    public void TheMissingAndTheUnreadable_ThrowWhatTheySay()
    {
        var manager = Manager(new CountingReader());

        Assert.Multiple(() =>
        {
            Assert.Throws<AssetNotFoundException>(() => manager.Load<Asset>("nowhere/missing.bin", Kept));
            Assert.Throws<NotSupportedException>(() => manager.Load<string>("a", Kept));
        });
    }

    // A reader working before it returns - as the model and texture readers do - must not hold up the caller.
    [TestCase(false)]
    [TestCase(true)]
    public void LoadAsync_ReadsOffTheCallersThread(bool kept)
    {
        var reader = new CountingReader { Blocking = TimeSpan.FromMilliseconds(300) };
        var manager = Manager(reader);

        var load = manager.LoadAsync<Asset>("a", kept ? Kept : null);

        Assert.Multiple(() =>
        {
            Assert.That(load.IsCompleted, Is.False);
            Assert.That(load.Result, Is.Not.Null);
        });
    }

    [Test]
    public void TheReader_IsGivenTheLoadsOptions()
    {
        var reader = new CountingReader();
        var manager = Manager(reader);

        manager.Load<Asset>("a", Kept);

        Assert.That(reader.LastOptions, Is.SameAs(Kept));
    }

    private static ContentManager Manager(CountingReader reader)
    {
        var manager = new ContentManager(new AdamantiumDependencyContainer(), new Satellites());
        manager.Resolvers.Add(new FileSystemContentResolver());
        manager.Readers[typeof(Asset)] = reader;
        manager.Embed("a", [1]);
        return manager;
    }

    private sealed class Asset : DisposableObject;

    private sealed class CountingReader : IContentReader
    {
        private int reads;

        public TimeSpan Delay { get; init; }

        public TimeSpan Blocking { get; init; }

        public bool FailFirst { get; init; }

        public int Reads => Volatile.Read(ref reads);

        public object LastOptions { get; private set; }

        public async Task<object> ReadContentAsync(IContentManager contentManager, ContentReaderParameters parameters)
        {
            var count = Interlocked.Increment(ref reads);
            LastOptions = parameters.Options;
            if (Blocking > TimeSpan.Zero)
            {
                Thread.Sleep(Blocking);
            }

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay);
            }

            if (FailFirst && count == 1)
            {
                throw new InvalidOperationException("the first read fails");
            }
            return new Asset();
        }
    }
}
