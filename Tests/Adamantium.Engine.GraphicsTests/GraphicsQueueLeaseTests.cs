using System.Collections.Generic;
using System.Threading.Tasks;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>
/// Every render device draws on a graphics queue of its own while there is one free. Handed out in turn and never given
/// back, the queues ran out after sixteen windows had opened and closed, and the next window drew on the main window's
/// queue from another thread - which lost the device.
/// </summary>
[TestFixture]
public class GraphicsQueueLeaseTests
{
    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    [Test]
    public void ADeviceOpenedAfterOthersClosed_DoesNotTakeTheQueueOfALiveOne()
    {
        var queueCount = (int)GpuFixture.Main.AvailableQueuesCount;
        Assume.That(queueCount, Is.GreaterThan(2), "a queue to spare beside the loader's and the live device's");
        var live = GpuFixture.CreateRenderDevice();
        HashSet<object> taken = [live.GraphicsQueueLock, GpuFixture.Main.ResourceLoaderDevice.GraphicsQueueLock];

        var shared = 0;
        for (var i = 0; i < queueCount; i++)
        {
            var device = GpuFixture.Main.CreateRenderDevice();
            if (taken.Contains(device.GraphicsQueueLock))
            {
                shared++;
            }

            GpuFixture.Main.RemoveDevice(device);
        }

        Assert.That(shared, Is.Zero);
    }

    // A lost device is reported once, by whichever call first finds it lost, so its owner replaces it once.
    [Test]
    public void ALostDevice_IsReportedOnce()
    {
        var main = MainGraphicsDevice.Create(new GraphicsDeviceFactory(), GpuFixture.Settings(), "LostDevice", false);
        try
        {
            var reports = 0;
            main.Lost += () => reports++;

            main.Observe(Result.Success);
            main.Observe(Result.ErrorDeviceLost);
            main.Observe(Result.ErrorDeviceLost);

            Assert.Multiple(() =>
            {
                Assert.That(main.IsLost, Is.True);
                Assert.That(reports, Is.EqualTo(1));
            });
        }
        finally
        {
            main.Dispose();
        }
    }

    // Waiting for the device to idle is a use of every queue: it waits its turn behind a submit or a present in progress.
    [Test]
    public void WaitingForTheDeviceToIdle_WaitsForAQueueInUse()
    {
        var device = GpuFixture.CreateRenderDevice();
        Task<Result> waiting;
        lock (device.GraphicsQueueLock)
        {
            waiting = Task.Run(() => GpuFixture.Main.DeviceWaitIdle());
            Assert.That(waiting.Wait(300), Is.False, "it must not idle the device while the queue is in use");
        }

        Assert.That(waiting.Wait(5000), Is.True, "and goes on once the queue is free");
    }
}
