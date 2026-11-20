using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.Multiverse.Input;
using Adamantium.Vulkan.Core;

namespace Adamantium.Multiverse.Remote;

/// <summary>The viewer's end of a universe host's channel: it sends the view's size and what the user does, and is told
/// which surfaces to show and when a frame is in them.</summary>
public sealed class UniverseViewerChannel : UniverseChannel
{
    private readonly IUniverseViewerReceiver _receiver;

    public UniverseViewerChannel(Stream stream, IUniverseViewerReceiver receiver) : base(stream)
    {
        _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
    }

    public void SendResize(uint width, uint height, float pixelsPerPoint)
    {
        lock (WriteGate)
        {
            var writer = StartMessage();
            writer.Write(width);
            writer.Write(height);
            writer.Write(pixelsPerPoint);
            FinishMessage(UniverseMessageKind.Resize);
        }
    }

    public void SendFrameAsked(ulong frame)
    {
        lock (WriteGate)
        {
            StartMessage().Write(frame);
            FinishMessage(UniverseMessageKind.FrameAsked);
        }
    }

    public void SendPointer(Vector2F position, bool isOver)
    {
        lock (WriteGate)
        {
            var writer = StartMessage();
            writer.Write(position.X);
            writer.Write(position.Y);
            writer.Write(isOver);
            FinishMessage(UniverseMessageKind.Pointer);
        }
    }

    public void SendFocus(bool hasKeyboard)
    {
        lock (WriteGate)
        {
            StartMessage().Write(hasKeyboard);
            FinishMessage(UniverseMessageKind.Focus);
        }
    }

    public void SendKeyboardInput(KeyboardInput input)
    {
        lock (WriteGate)
        {
            var writer = StartMessage();
            writer.Write((int)input.Key);
            writer.Write((int)input.InputType);
            FinishMessage(UniverseMessageKind.Keyboard);
        }
    }

    public void SendMouseInput(MouseInput input)
    {
        lock (WriteGate)
        {
            var writer = StartMessage();
            writer.Write((int)input.Button);
            writer.Write((int)input.InputType);
            writer.Write(input.WheelDelta);
            writer.Write(input.Delta.X);
            writer.Write(input.Delta.Y);
            writer.Write(input.ClickCount);
            writer.Write(input.Position.HasValue);
            if (input.Position is { } position)
            {
                writer.Write(position.X);
                writer.Write(position.Y);
            }

            FinishMessage(UniverseMessageKind.Mouse);
        }
    }

    public void SendGestureInput(GestureInput input)
    {
        lock (WriteGate)
        {
            var writer = StartMessage();
            writer.Write(input.Translation.X);
            writer.Write(input.Translation.Y);
            writer.Write(input.Scale);
            writer.Write(input.Rotation);
            FinishMessage(UniverseMessageKind.Gesture);
        }
    }

    public void SendText(string text)
    {
        lock (WriteGate)
        {
            StartMessage().Write(text ?? string.Empty);
            FinishMessage(UniverseMessageKind.Text);
        }
    }

    public void SendStop() => SendEmpty(UniverseMessageKind.Stop);

    protected override void Receive(UniverseMessageKind kind, BinaryReader payload)
    {
        switch (kind)
        {
            case UniverseMessageKind.Hello:
                _receiver.Greet(new RemoteHello(payload.ReadInt32(), new Guid(payload.ReadBytes(16))));
                break;
            case UniverseMessageKind.Surface:
                _receiver.ShowSurface(ReadSurface(payload));
                break;
            case UniverseMessageKind.FrameReady:
                _receiver.ShowFrame(payload.ReadUInt64());
                break;
            case UniverseMessageKind.Stopped:
                _receiver.EndUniverse();
                break;
        }
    }

    protected override void LoseOtherEnd() => _receiver.LoseHost();

    private static RemoteSurface ReadSurface(BinaryReader payload)
    {
        var width = payload.ReadUInt32();
        var height = payload.ReadUInt32();
        var format = (SurfaceFormat)(Format)payload.ReadInt32();
        var handleType = (SharedHandleType)payload.ReadInt32();
        var buffers = new RemoteSurfaceBuffer[payload.ReadInt32()];
        for (var i = 0; i < buffers.Length; i++)
        {
            buffers[i] = new RemoteSurfaceBuffer(payload.ReadInt64(), payload.ReadUInt64(), payload.ReadInt64(),
                payload.ReadInt64());
        }

        return new RemoteSurface { Width = width, Height = height, Format = format, HandleType = handleType, Buffers = buffers };
    }
}
