using System;
using System.Collections.Concurrent;
using System.IO.Pipes;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.Multiverse.Input;
using Adamantium.Multiverse.Remote;
using NUnit.Framework;

namespace Adamantium.EngineTests.Remote;

/// <summary>The channel between a universe host and its viewer over a real pipe: every message arrives as sent and in
/// order, both ways, and an end that goes away is noticed by the other.</summary>
[TestFixture]
public class UniverseChannelTests
{
    private NamedPipeServerStream _hostPipe;
    private NamedPipeClientStream _viewerPipe;
    private RecordingHostReceiver _host;
    private RecordingViewerReceiver _viewer;
    private UniverseHostChannel _hostChannel;
    private UniverseViewerChannel _viewerChannel;

    [SetUp]
    public void Connect()
    {
        var name = "adamantium-universe-channel-" + Guid.NewGuid().ToString("N");
        _hostPipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        _viewerPipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        var waiting = _hostPipe.WaitForConnectionAsync();
        _viewerPipe.Connect(5000);
        waiting.Wait(5000);
        _host = new RecordingHostReceiver();
        _viewer = new RecordingViewerReceiver();
        _hostChannel = new UniverseHostChannel(_hostPipe, _host);
        _viewerChannel = new UniverseViewerChannel(_viewerPipe, _viewer);
        _hostChannel.Start();
        _viewerChannel.Start();
    }

    [TearDown]
    public void Disconnect()
    {
        _hostChannel?.Dispose();
        _viewerChannel?.Dispose();
        _hostChannel = null;
        _viewerChannel = null;
    }

    [Test]
    public void WhatTheHostSends_TheViewerIsTold_InOrder()
    {
        var uuid = Guid.NewGuid();
        _hostChannel.SendHello(new RemoteHello(4242, uuid));
        _hostChannel.SendSurface(new RemoteSurface
        {
            Width = 1280,
            Height = 720,
            Format = SurfaceFormat.B8G8R8A8.UNorm,
            HandleType = SharedHandleType.OpaqueWin32,
            Buffers = [new RemoteSurfaceBuffer(11, 4096, 12, 13), new RemoteSurfaceBuffer(21, 4096, 22, 23)]
        });
        _hostChannel.SendFrameReady(7);
        _hostChannel.SendStopped();

        Assert.That(Take(_viewer.Messages, 4), Is.EqualTo((string[])
        [
            $"Greet 4242 {uuid}",
            "ShowSurface 1280x720 2",
            "ShowFrame 7",
            "EndUniverse"
        ]));
        var surface = _viewer.LastSurface;
        Assert.Multiple(() =>
        {
            Assert.That(surface.Format, Is.EqualTo((SurfaceFormat)SurfaceFormat.B8G8R8A8.UNorm));
            Assert.That(surface.HandleType, Is.EqualTo(SharedHandleType.OpaqueWin32));
            Assert.That(surface.Buffers[1], Is.EqualTo(new RemoteSurfaceBuffer(21, 4096, 22, 23)));
        });
    }

    [Test]
    public void WhatTheViewerSends_TheHostIsTold_InOrder()
    {
        _viewerChannel.SendResize(640, 360, 1.5f);
        _viewerChannel.SendFrameAsked(3);
        _viewerChannel.SendPointer(new Vector2F(10.5f, 20.25f), true);
        _viewerChannel.SendFocus(true);
        _viewerChannel.SendKeyboardInput(new KeyboardInput { Key = Keys.W, InputType = InputType.Down });
        _viewerChannel.SendMouseInput(new MouseInput { Button = MouseButton.Left, InputType = InputType.Down, ClickCount = 2 });
        _viewerChannel.SendMouseInput(new MouseInput
        {
            Button = MouseButton.None, InputType = InputType.Wheel, WheelDelta = -120, Position = new Vector2F(5, 6)
        });
        _viewerChannel.SendGestureInput(new GestureInput { Translation = new Vector2F(1, 2), Scale = 1.25f, Rotation = 0.5f });
        _viewerChannel.SendText("привет");
        _viewerChannel.SendStop();

        Assert.That(Take(_host.Messages, 10), Is.EqualTo((string[])
        [
            "Resize 640x360 at 1.5",
            "AskFrame 3",
            "MovePointer 10.5,20.25 True",
            "Focus True",
            "Keyboard W Down",
            "Mouse Left Down 0 0,0 2 none",
            "Mouse None Wheel -120 0,0 0 5,6",
            "Gesture 1,2 1.25 0.5",
            "Text привет",
            "Stop"
        ]));
    }

    [Test]
    public void AnEndThatGoesAway_IsNoticedByTheOther()
    {
        _viewerChannel.Dispose();
        Assert.That(Take(_host.Messages, 1), Is.EqualTo((string[])["LoseViewer"]));

        _hostChannel.SendFrameReady(1);
    }

    private static string[] Take(BlockingCollection<string> messages, int count)
    {
        var taken = new string[count];
        for (var i = 0; i < count; i++)
        {
            Assert.That(messages.TryTake(out taken[i], TimeSpan.FromSeconds(5)), Is.True, $"message {i + 1} of {count}");
        }

        return taken;
    }
}
