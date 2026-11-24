using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.Multiverse.Input;
using Adamantium.Multiverse.Remote;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.EngineTests.Remote;

/// <summary>The connection between a universe host and its viewer over a real pipe: the viewer calls the host's service
/// and is answered, every notification arrives as sent and in order, the host tells the viewer what to show, and a
/// failure reaches the caller with its reason.</summary>
[TestFixture]
public class UniverseConnectionTests
{
    private RecordingHostService _service;
    private RecordingViewer _viewer;
    private UniverseViewerConnection _hostEnd;
    private UniverseHostConnection _viewerEnd;

    [SetUp]
    public void Connect()
    {
        var pipeName = "adamantium-universe-connection-" + Guid.NewGuid().ToString("N");
        _service = new RecordingHostService();
        _viewer = new RecordingViewer();
        var accepting = UniverseViewerConnection.AcceptAsync(pipeName, _service, CancellationToken.None);
        _viewerEnd = UniverseHostConnection.ConnectAsync(pipeName, _viewer, CancellationToken.None).Result;
        _hostEnd = accepting.Result;
    }

    [TearDown]
    public void Disconnect()
    {
        _viewerEnd?.Dispose();
        _hostEnd?.Dispose();
        _viewerEnd = null;
        _hostEnd = null;
    }

    [Test]
    public void TheHost_KnowsTheViewersProcess() =>
        Assert.That(_hostEnd.ViewerProcessId, Is.EqualTo(Environment.ProcessId));

    [Test]
    public async Task WhatTheViewerCalls_TheHostIsCalled_InOrder_AndAnswers()
    {
        var host = _viewerEnd.Host;
        var scene = Path.GetTempFileName();
        try
        {
            await host.LaunchAsync(new UniverseLaunch { ScenePath = scene }, CancellationToken.None);
        }
        finally
        {
            File.Delete(scene);
        }

        host.Resize(640, 360, 1.5f);
        host.AskFrame(3);
        host.MovePointer(10.5f, 20.25f, true);
        host.Focus(true);
        host.TakeKeyboardInput(new KeyboardInput { Key = Keys.W, InputType = InputType.Down });
        host.TakeMouseInput(new MouseInput { Button = MouseButton.Left, InputType = InputType.Down, ClickCount = 2 });
        host.TakeMouseInput(new MouseInput
        {
            Button = MouseButton.None, InputType = InputType.Wheel, WheelDelta = -120, Position = new Vector2<float>(5, 6)
        });
        host.TakeGestureInput(new GestureInput { Translation = new Vector2<float>(1, 2), Scale = 1.25f, Rotation = 0.5f });
        host.TakeText("привет");
        await host.StopAsync(CancellationToken.None);

        Assert.That(Take(_service.Calls, 11), Is.EqualTo((string[])
        [
            "Launch .tmp",
            "Resize 640x360 at 1.5",
            "AskFrame 3",
            "MovePointer 10.5,20.25 True",
            "Focus True",
            "Keyboard W Down",
            "Mouse Left Down 0 2 none",
            "Mouse None Wheel -120 0 5,6",
            "Gesture 1,2 1.25 0.5",
            "Text привет",
            "Stop"
        ]));
        Assert.Multiple(async () =>
        {
            Assert.That(await host.CaptureSceneAsync(CancellationToken.None), Is.EqualTo("<Scene />"));
            Assert.That(await host.CaptureFrameAsync(CancellationToken.None), Is.EqualTo(new byte[] { 1, 2, 3 }));
        });
    }

    [Test]
    public void ALaunchThatFails_FailsTheCaller_WithTheReason()
    {
        var failure = Assert.ThrowsAsync<UniverseHostException>(() => _viewerEnd.Host.LaunchAsync(
            new UniverseLaunch { ScenePath = "Missing.ascene" }, CancellationToken.None));
        Assert.That(failure.Message, Does.Contain("Missing.ascene"));
    }

    [Test]
    public void WhatTheHostTells_TheViewerIsTold_InOrder()
    {
        _hostEnd.Viewer.Greet(new RemoteHello(42, Guid.NewGuid()));
        _hostEnd.Viewer.ShowSurface(new RemoteSurface
        {
            Width = 1280,
            Height = 720,
            Format = SurfaceFormat.B8G8R8A8.UNorm,
            HandleType = SharedHandleType.OpaqueWin32,
            Buffers = [new RemoteSurfaceBuffer(11, 4096, 12, 13), new RemoteSurfaceBuffer(21, 4096, 22, 23)]
        });
        _hostEnd.Viewer.ShowFrame(7);
        _hostEnd.Viewer.EndUniverse();

        Assert.That(Take(_viewer.Calls, 4),
            Is.EqualTo((string[])["Greet 42", "ShowSurface 1280x720 2", "ShowFrame 7", "EndUniverse"]));
        Assert.Multiple(() =>
        {
            Assert.That(_viewer.LastSurface.Format, Is.EqualTo(Format.B8G8R8A8_UNORM));
            Assert.That(_viewer.LastSurface.HandleType, Is.EqualTo(SharedHandleType.OpaqueWin32));
            Assert.That(_viewer.LastSurface.Buffers[1], Is.EqualTo(new RemoteSurfaceBuffer(21, 4096, 22, 23)));
        });
    }

    // A host held by a debugger has its thread pool held too; only threads the debugger lets run go on. The connection
    // hears and tells on threads of its own: with every pool thread busy, a size given still reaches the host, and a
    // frame told still reaches the viewer.
    [Test]
    public void WithEveryPoolThreadBusy_TheConnectionStillCarries_BothWays()
    {
        using var busy = new ManualResetEventSlim();
        ThreadPool.GetMinThreads(out var minWorkers, out var minPorts);
        ThreadPool.GetMaxThreads(out var maxWorkers, out var maxPorts);
        var workers = Environment.ProcessorCount;
        try
        {
            ThreadPool.SetMinThreads(workers, minPorts);
            ThreadPool.SetMaxThreads(workers, maxPorts);
            for (var i = 0; i < workers * 4; i++)
            {
                ThreadPool.UnsafeQueueUserWorkItem(_ => busy.Wait(), null);
            }

            _viewerEnd.Host.Resize(320, 180, 1);
            _hostEnd.Viewer.ShowFrame(9);

            Assert.Multiple(() =>
            {
                Assert.That(Take(_service.Calls, 1), Is.EqualTo((string[])["Resize 320x180 at 1"]));
                Assert.That(Take(_viewer.Calls, 1), Is.EqualTo((string[])["ShowFrame 9"]));
            });
        }
        finally
        {
            busy.Set();
            ThreadPool.SetMaxThreads(maxWorkers, maxPorts);
            ThreadPool.SetMinThreads(minWorkers, minPorts);
        }
    }

    // Called off by its caller, a call is done at once - the host told - however long the host would take.
    [Test]
    public void ACallCalledOff_IsCanceledAtOnce()
    {
        _service.HeldScene = new TaskCompletionSource<string>();
        using var calledOff = new CancellationTokenSource();
        var capturing = _viewerEnd.Host.CaptureSceneAsync(calledOff.Token);

        calledOff.Cancel();

        Assert.That(() => capturing.Wait(TimeSpan.FromSeconds(5)),
            Throws.InstanceOf<AggregateException>().With.InnerException.InstanceOf<TaskCanceledException>());
    }

    [Test]
    public void AnEndThatGoesAway_ClosesTheOther()
    {
        _viewerEnd.Dispose();
        Assert.That(_hostEnd.Completion.Wait(TimeSpan.FromSeconds(5)), Is.True);
    }

    private static string[] Take(BlockingCollection<string> calls, int count)
    {
        var taken = new string[count];
        for (var i = 0; i < count; i++)
        {
            Assert.That(calls.TryTake(out taken[i], TimeSpan.FromSeconds(5)), Is.True, $"call {i + 1} of {count}");
        }

        return taken;
    }
}
