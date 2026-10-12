using System.Collections.Generic;
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
}
