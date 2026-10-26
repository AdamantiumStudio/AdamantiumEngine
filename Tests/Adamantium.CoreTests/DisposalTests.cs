using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Core;
using NUnit.Framework;

namespace Adamantium.CoreTests;

/// <summary>Things given to free are freed last first, each once; a disposable is disposed once however many threads
/// ask; what is freed by reference is left default.</summary>
[TestFixture]
public class DisposalTests
{
    private sealed class Counted(List<string> log, string name) : IDisposable
    {
        public int Times { get; private set; }

        public void Dispose()
        {
            Times++;
            log.Add(name);
        }
    }

    private sealed class Owner : DisposableObject
    {
        public T Keep<T>(T item) => ToDispose(item);

        public void Free<T>(ref T item) => RemoveAndDispose(ref item);
    }

    private sealed class Once : DisposableBase
    {
        public int Times;

        protected override void Dispose(bool disposeManaged) => Interlocked.Increment(ref Times);
    }

    [Test]
    public void WhatIsCollected_IsFreedLastFirst_EachOnce()
    {
        var log = new List<string>();
        var first = new Counted(log, "first");
        var second = new Counted(log, "second");
        var collector = new DisposeCollector();
        collector.Collect(first);
        collector.Collect(second);
        collector.Collect(first);
        collector.Collect(Utilities.AllocateMemory(64));

        collector.Dispose();

        Assert.That(log, Is.EqualTo(new[] { "second", "first" }));
        Assert.That(collector.Count, Is.Zero);
    }

    [Test]
    public void SomethingFreedByReference_IsLeftNull()
    {
        var log = new List<string>();
        using var owner = new Owner();
        var kept = owner.Keep(new Counted(log, "kept"));
        var held = kept;

        owner.Free(ref kept);

        Assert.That(kept, Is.Null);
        Assert.That(held.Times, Is.EqualTo(1));
    }

    [Test]
    public void SomethingNotHeld_IsLeftAsItIs()
    {
        var log = new List<string>();
        using var owner = new Owner();
        var stranger = new Counted(log, "stranger");

        owner.Free(ref stranger);

        Assert.That(stranger, Is.Not.Null);
        Assert.That(stranger.Times, Is.Zero);
    }

    [Test]
    public void OnlyDisposablesAndPointers_AreCollected() =>
        Assert.That(() => new DisposeCollector().Collect("text"), Throws.ArgumentException);

    [Test]
    public void ADisposableAskedByManyThreads_IsDisposedOnce()
    {
        var once = new Once();
        var disposed = 0;
        once.Disposed += (_, _) => Interlocked.Increment(ref disposed);

        Parallel.For(0, 64, _ => once.Dispose());

        Assert.That((once.Times, disposed, once.IsDisposed), Is.EqualTo((1, 1, true)));
    }

    private struct Mixed
    {
        public int Number;
        public double Value;
        public bool Flag;
    }

    [Test]
    public void ValuesWrittenAndRead_AreThemselves()
    {
        var memory = Utilities.AllocateMemory(256);
        try
        {
            var mixed = new Mixed { Number = 7, Value = 2.5, Flag = true };
            Utilities.Write(memory, ref mixed);
            Assert.That(Utilities.Read<Mixed>(memory), Is.EqualTo(mixed));

            int[] numbers = [1, 2, 3, 4, 5];
            var after = Utilities.Write(memory, numbers, 1, 3);
            var read = new int[5];
            Assert.That(Utilities.Read(memory, read, 2, 3), Is.EqualTo(after));
            Assert.That(read, Is.EqualTo(new[] { 0, 0, 2, 3, 4 }));

            Utilities.ClearMemory(memory, 0xAB, 4);
            Assert.That(Utilities.Read<uint>(memory), Is.EqualTo(0xABABABABu));
        }
        finally
        {
            Utilities.FreeMemory(memory);
        }
    }

    [Test]
    public void Collections_AreComparedElementByElement()
    {
        Assert.That(Utilities.Compare(new[] { 1, 2 }, new List<int> { 1, 2 }), Is.True);
        Assert.That(Utilities.Compare(new[] { 1, 2 }, new[] { 1, 2, 3 }), Is.False);
        Assert.That(Utilities.Compare(new[] { 1, 2 }, new[] { 2, 1 }), Is.False);
        Assert.That(Utilities.Compare(null, null), Is.True);
        Assert.That(Utilities.Compare(new int[0], null), Is.False);
    }
}
