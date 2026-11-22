using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
        to.CollectionChanged += (_, e) => from.Remove(e.NewItems!.Cast<string>());

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

    // What one change to a collection told: its action, where, what, and how many were in once it was made.
    private static List<string> ToldOf(TrackingCollection<string> c)
    {
        var told = new List<string>();
        c.CollectionChanged += (_, e) => told.Add(e.Action switch
        {
            NotifyCollectionChangedAction.Add => "+" + e.NewStartingIndex + ":" + string.Join(",", e.NewItems!.Cast<string>()),
            NotifyCollectionChangedAction.Remove => "-" + e.OldStartingIndex + ":" + string.Join(",", e.OldItems!.Cast<string>()),
            _ => e.Action.ToString(),
        } + " of " + c.Count);
        return told;
    }

    // A range is one change: one Add with all its items, from where they stand.
    [Test]
    public void ARange_IsToldAsOneChange_WithAllItsItems()
    {
        var c = Of("A");
        var told = ToldOf(c);

        c.AddRange(["B", "C"]);
        c.AddRange(new List<string> { "D" });
        c.InsertRange(1, new List<string> { "X", "Y" });
        c.RemoveRange(0, 2);
        c.Insert(0, "Z");
        c.RemoveAt(0);

        Assert.That(told, Is.EqualTo(new[]
        {
            "+1:B,C of 3", "+3:D of 4", "+1:X,Y of 6", "-0:A,X of 4", "+0:Z of 5", "-0:Z of 4",
        }));
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

    // Every way in and out goes through the one pair of storage hooks, a range at once.
    [Test]
    public void EveryChange_IsStoredThroughInsertItemsAndRemoveItems_ARangeAtOnce()
    {
        var c = new OnlyEven();

        c.AddRange(new List<int> { 2, 4, 6 });
        c.Insert(1, 8);
        c.InsertRange(0, [10, 12]);
        c.RemoveRange(1, 3);
        c.RemoveAt(0);
        c.Remove(6);

        Assert.Multiple(() =>
        {
            Assert.That(c, Is.EqualTo(new[] { 4 }));
            Assert.That(c.Stored, Is.EqualTo(new[] { "+0:3", "+1:1", "+0:2", "-1:3", "-0:1", "-1:1" }));
            Assert.That(c.Told, Is.EqualTo(new[] { "0:2,4,6", "1:8", "0:10,12" }));
        });
    }

    // A range with an item the collection refuses is not stored at all, and nothing is told.
    [Test]
    public void ARangeWithAnItemRefused_LeavesTheCollectionAsItWas()
    {
        var c = new OnlyEven();
        c.AddRange([2]);

        Assert.Throws<ArgumentException>(() => c.AddRange([4, 5, 6]));
        Assert.Multiple(() =>
        {
            Assert.That(c, Is.EqualTo(new[] { 2 }));
            Assert.That(c.Told, Is.EqualTo(new[] { "0:2" }));
        });
    }

    private sealed class OnlyEven : AdamantiumCollection<int>
    {
        public List<string> Stored { get; } = [];

        public List<string> Told { get; } = [];

        protected override void CheckItems(ReadOnlySpan<int> range)
        {
            foreach (var item in range)
            {
                if (item % 2 != 0) throw new ArgumentException("odd", nameof(range));
            }
        }

        protected override void InsertItems(int index, ReadOnlySpan<int> range)
        {
            Stored.Add("+" + index + ":" + range.Length);
            base.InsertItems(index, range);
        }

        protected override void RemoveItems(int index, int count)
        {
            Stored.Add("-" + index + ":" + count);
            base.RemoveItems(index, count);
        }

        protected override void OnInsert(int index, ReadOnlySpan<int> range) =>
            Told.Add(index + ":" + string.Join(",", range.ToArray()));
    }

    [Test]
    public void InsertRange_InTheMiddle_PutsTheItemsThere()
    {
        var c = Of("A", "B", "C");

        c.InsertRange(1, new List<string> { "X", "Y" });

        Assert.That(c, Is.EqualTo(new[] { "A", "X", "Y", "B", "C" }));
    }

    [Test]
    public void InsertRange_OfItself_InTheMiddle_InsertsWhatItHeldBefore()
    {
        var c = Of("A", "B");

        c.InsertRange(1, c);

        Assert.That(c, Is.EqualTo(new[] { "A", "A", "B", "B" }));
    }

    [Test]
    public void InsertRange_PastTheEnd_Appends()
    {
        var c = Of("A");

        c.InsertRange(5, ["B", "C"]);

        Assert.That(c, Is.EqualTo(new[] { "A", "B", "C" }));
    }


    // Regression: an index past the end took the count down before failing - or, within the storage, wiped the last
    // item and told a default one removed.
    [Test]
    public void RemoveAt_PastTheEnd_Throws_AndKeepsTheItems()
    {
        var c = Of("A", "B");

        Assert.Throws<ArgumentOutOfRangeException>(() => c.RemoveAt(2));
        Assert.That(c, Is.EqualTo(new[] { "A", "B" }));
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
