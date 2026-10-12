using System;
using System.Collections.Generic;
using System.Threading;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>
/// Resources are created and disposed on more than one thread: textures are built from background tasks while the
/// render thread works. The device's registry of them must stay whole under that.
/// </summary>
[TestFixture]
public class ResourceRegistryConcurrencyTests
{
    private const int Threads = 8;
    private const int PerThread = 20000;

    private sealed class Probe : GraphicsResource
    {
        public Probe(IGraphicsDevice device) : base(device)
        {
        }
    }

    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    [Test]
    public void ResourcesMadeAndDisposedOnManyThreads_LeaveTheRegistryAsItWas()
    {
        var device = (GraphicsDevice)GpuFixture.CreateRenderDevice();
        var before = device.RegisteredResourceCount;
        var start = new Barrier(Threads);
        var failures = new List<Exception>();
        var workers = new Thread[Threads];
        for (var t = 0; t < Threads; t++)
        {
            workers[t] = new Thread(() =>
            {
                try
                {
                    start.SignalAndWait();
                    for (var i = 0; i < PerThread; i++)
                    {
                        new Probe(device).Dispose();
                    }
                }
                catch (Exception e)
                {
                    lock (failures)
                    {
                        failures.Add(e);
                    }
                }
            }) { IsBackground = true };
            workers[t].Start();
        }

        foreach (var worker in workers)
        {
            Assert.That(worker.Join(TimeSpan.FromMinutes(1)), Is.True, "every thread finishes");
        }

        Assert.Multiple(() =>
        {
            Assert.That(failures, Is.Empty, "no thread fails while registering or unregistering");
            Assert.That(device.RegisteredResourceCount, Is.EqualTo(before), "every probe was unregistered");
        });
    }
}
