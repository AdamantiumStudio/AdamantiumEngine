using NUnit.Framework;

namespace Adamantium.UniverseHost.Tests;

/// <summary>The wait for a drawing where the game stands: at once with none under way; else done as it ends, on the
/// drawing's own thread - at a debugger's stop the pool is held - and called off at once by its token.</summary>
[TestFixture]
public class DrawingWatchTests
{
    [Test]
    public void NoDrawingUnderWay_IsDoneAtOnce()
    {
        var watch = new DrawingWatch();

        Assert.That(watch.WaitTillEndedAsync(CancellationToken.None).IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public async Task ADrawingUnderWay_IsWaitedFor_AndWhoeverWaitsGoesOnOnTheDrawingsThread()
    {
        var watch = new DrawingWatch();
        watch.Begin();
        var waiting = Wait(watch);
        Assert.That(waiting.IsCompleted, Is.False, "the drawing is under way");

        var drawingThread = new Thread(watch.End);
        drawingThread.Start();
        drawingThread.Join();

        Assert.That(await waiting, Is.EqualTo(drawingThread.ManagedThreadId), "went on as the drawing ended, on its thread");
    }

    [Test]
    public void AWaitCalledOff_IsCanceledAtOnce_TheDrawingStillUnderWay()
    {
        var watch = new DrawingWatch();
        watch.Begin();
        using var calledOff = new CancellationTokenSource();
        var waiting = watch.WaitTillEndedAsync(calledOff.Token);

        calledOff.Cancel();

        Assert.That(waiting.IsCanceled, Is.True);
    }

    // Goes on on whichever thread ends the drawing, as a link's answer does: it says which.
    private static async Task<int> Wait(DrawingWatch watch)
    {
        await watch.WaitTillEndedAsync(CancellationToken.None).ConfigureAwait(false);
        return Environment.CurrentManagedThreadId;
    }
}
