using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.Collections;
using NUnit.Framework;

namespace Adamantium.CoreTests;

// Regression: AdamantiumCollection.Insert(index, item) stored the item at the END (currentIndex) instead of at `index`,
// so an insert-in-the-middle appended it and duplicated the element that had been at `index`. It surfaced as a tab
// dragged to a middle slot "flying to the end" (the panel's Children.Insert during the reorder appended the moved tab).
[TestFixture]
public class AdamantiumCollectionTests
{
    private static TrackingCollection<string> Of(params string[] items)
    {
        var c = new TrackingCollection<string>();
        foreach (var i in items) c.Add(i);
        return c;
    }

    // Regression: the params constructor never made its storage, so handing it an array threw on the first item.
    [Test]
    public void MadeFromAnArray_HoldsItsItems()
    {
        double[] items = [1, 2, 3];

        var c = new AdamantiumCollection<double>(items);

        Assert.That(c, Is.EqualTo(items));
    }

    [Test]
    public void Insert_InTheMiddle_PutsItemAtIndex_NotAtTheEnd()
    {
        var c = Of("A", "B", "C", "D");

        c.Insert(2, "X");

        Assert.That(c.Count, Is.EqualTo(5));
        Assert.That(new[] { c[0], c[1], c[2], c[3], c[4] }, Is.EqualTo(new[] { "A", "B", "X", "C", "D" }));
    }

    [Test]
    public void Insert_AtStart_ShiftsEverythingRight()
    {
        var c = Of("A", "B");
        c.Insert(0, "X");
        Assert.That(new[] { c[0], c[1], c[2] }, Is.EqualTo(new[] { "X", "A", "B" }));
    }

    [Test]
    public void Insert_AtEnd_Appends()
    {
        var c = Of("A", "B");
        c.Insert(2, "C");
        Assert.That(new[] { c[0], c[1], c[2] }, Is.EqualTo(new[] { "A", "B", "C" }));
    }

    // The exact move-reorder pattern (RemoveAt + Insert), as a tab drag commits it.
    [Test]
    public void RemoveThenInsert_ReordersToTheTargetSlot()
    {
        var c = Of("A", "B", "C", "D", "E");
        var moved = c[1];       // "B"
        c.RemoveAt(1);          // A C D E
        c.Insert(3, moved);     // A C D B E
        Assert.That(new[] { c[0], c[1], c[2], c[3], c[4] }, Is.EqualTo(new[] { "A", "C", "D", "B", "E" }));
    }

    // Regression: RemoveRange removed every OTHER element (RemoveInternal(i) for i in [index, index+count), but each
    // remove shifts the rest down) and could run off the end.
    [Test]
    public void RemoveRange_RemovesTheConsecutiveBlock()
    {
        var c = Of("A", "B", "C", "D", "E");
        c.RemoveRange(1, 2);   // drop B, C
        Assert.That(c.Count, Is.EqualTo(3));
        Assert.That(new[] { c[0], c[1], c[2] }, Is.EqualTo(new[] { "A", "D", "E" }));
    }

    [Test]
    public void RemoveRange_ClampsCountToWhatRemains()
    {
        var c = Of("A", "B", "C");
        c.RemoveRange(1, 10);
        Assert.That(c.Count, Is.EqualTo(1));
        Assert.That(c[0], Is.EqualTo("A"));
    }

    [Test]
    public void AddRange_OfItself_DoublesItsItems()
    {
        var c = Of("A", "B");

        c.AddRange(c);

        Assert.That(c, Is.EqualTo(new[] { "A", "B", "A", "B" }));
    }

    [Test]
    public void AddRange_OfAQueryOverItself_AddsWhatItHeldBefore()
    {
        var c = Of("A", "B", "C");

        c.AddRange(c.Where(item => item != "B"));

        Assert.That(c, Is.EqualTo(new[] { "A", "B", "C", "A", "C" }));
    }

    [Test]
    public void AddRange_OfAViewOfItself_AddsWhatItHeldBefore()
    {
        var c = Of("A", "B");

        c.AddRange(new ReadOnlyTrackingCollection<string>(c));

        Assert.That(c, Is.EqualTo(new[] { "A", "B", "A", "B" }));
    }

    [Test]
    public void AddRange_OfItemsAHandlerTakesFromTheirList_AddsThemAll()
    {
        var from = Of("B", "C", "D");
        var to = Of("A");
        to.CollectionChanged += (_, e) => from.Remove((string)e.NewItems![0]);

        to.AddRange(from);

        Assert.Multiple(() =>
        {
            Assert.That(to, Is.EqualTo(new[] { "A", "B", "C", "D" }));
            Assert.That(from, Is.Empty);
        });
    }

    [Test]
    public void AddRange_OfAnyKindOfSource_AppendsItsItemsInOrder()
    {
        var c = Of("A");

        c.AddRange((IEnumerable<string>)new[] { "B", "C" });
        c.AddRange(new List<string> { "D" });
        c.AddRange(new LinkedList<string>(["E", "F"]));
        c.AddRange(Enumerable.Range(0, 2).Select(i => "G" + i));
        c.AddRange("H I".Split(' ').AsSpan());
        c.AddRange((IEnumerable<string>)null);

        Assert.That(c, Is.EqualTo(new[] { "A", "B", "C", "D", "E", "F", "G0", "G1", "H", "I" }));
    }

    [Test]
    public void AddRange_OfASpan_TellsEachItemAddedAtItsIndex()
    {
        var c = Of("A");
        var added = new List<string>();
        c.CollectionChanged += (_, e) => added.Add(e.NewStartingIndex + ":" + e.NewItems![0]);

        c.AddRange(["B", "C"]);

        Assert.That(added, Is.EqualTo(new[] { "1:B", "2:C" }));
    }

    // What was passed is added, though a handler of the adds changes the array it came in.
    [Test]
    public void AddRange_OfAnArrayAHandlerChanges_AddsWhatWasPassed()
    {
        var c = Of("A");
        string[] buffer = ["B", "C", "D"];
        c.CollectionChanged += (_, _) => Array.Fill(buffer, "X");

        c.AddRange(buffer);
        c.AddRange((IEnumerable<string>)new[] { "E", "F" });

        Assert.That(c, Is.EqualTo(new[] { "A", "B", "C", "D", "E", "F" }));
    }

    [Test]
    public void AddRange_OnAFixedSizeCollection_Throws_HoweverItIsPassed()
    {
        var c = new AdamantiumCollection<int>(4);

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => c.AddRange(Array.Empty<int>()));
            Assert.Throws<InvalidOperationException>(() => c.AddRange(new List<int>()));
        });
    }

    // A collection that checks its items still sees every one of a range, each at the end it has grown to.
    [Test]
    public void AddRange_GoesThroughInsertItem_ItemByItem()
    {
        var c = new OnlyEven();

        c.AddRange(new List<int> { 1, 2, 3, 4, 6 });
        c.AddRange([7, 8]);

        Assert.Multiple(() =>
        {
            Assert.That(c, Is.EqualTo(new[] { 2, 4, 6, 8 }));
            Assert.That(c.Told, Is.EqualTo(new[] { "0:2", "1:4", "2:6", "3:8" }));
        });
    }

    private sealed class OnlyEven : AdamantiumCollection<int>
    {
        public List<string> Told { get; } = [];

        protected override void InsertItem(int index, int item)
        {
            if (item % 2 == 0) base.InsertItem(index, item);
        }

        protected override void OnInsert(int index, int item)
        {
            if (item % 2 == 0) Told.Add(index + ":" + item);
        }
    }

    [Test]
    public void ARemoveWatched_TellsTheItemRemoved()
    {
        var c = Of("A", "B");
        object removed = null;
        c.CollectionChanged += (_, e) => removed = e.OldItems![0];

        c.RemoveAt(1);

        Assert.That(removed, Is.EqualTo("B"));
    }

    [Test]
    public void AddRange_StillTellsEachItemAdded()
    {
        var c = Of("A");
        var told = new List<(object Item, int Index)>();
        c.CollectionChanged += (_, e) => told.Add((e.NewItems![0], e.NewStartingIndex));

        c.AddRange(new List<string> { "B", "C" });

        Assert.That(told, Is.EqualTo(new List<(object, int)> { ("B", 1), ("C", 2) }));
    }

    // Unwatched, a collection filled with many items costs about their storage: an announcement of each was made for
    // nobody - a list per item - and a mesh of a map tile's areas paid megabytes for it.
    [Test]
    public void Filling_AnUnwatchedCollection_AllocatesAboutItsStorage()
    {
        var items = new double[10_000];
        _ = new TrackingCollection<double>(items);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var c = new TrackingCollection<double>(items);
        var filled = GC.GetAllocatedBytesForCurrentThread() - before;
        c.Capacity = items.Length + 10;
        before = GC.GetAllocatedBytesForCurrentThread();
        c.Add(1);
        c.Insert(0, 2);
        c.RemoveAt(0);
        var changed = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Multiple(() =>
        {
            Assert.That(filled, Is.LessThan(items.Length * sizeof(double) * 3), "its storage and a snapshot of the items");
            Assert.That(changed, Is.Zero, "an item added or removed with room to spare");
        });
    }

    // A view on another thread reads the items under the lock their changes are announced under - through the read-only
    // wrapper too, which is what an entity's Dependencies and the world's roots are.
    [Test]
    public void TheLockOfTheChanges_IsFoundThroughTheReadOnlyWrapper()
    {
        var c = Of("A", "B");
        var view = (System.Collections.ICollection)new ReadOnlyTrackingCollection<string>(c);
        var copy = new object[2];

        view.CopyTo(copy, 0);

        Assert.That(view.SyncRoot, Is.SameAs(c.SyncRoot));
        Assert.That(copy, Is.EqualTo(new object[] { "A", "B" }));
    }
}
