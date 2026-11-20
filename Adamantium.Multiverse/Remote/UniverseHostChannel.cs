using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.Multiverse.Input;

namespace Adamantium.Multiverse.Remote;

/// <summary>The universe host's end of its channel: it sends what the viewer shows and is told what to draw and what
/// the user does.</summary>
public sealed class UniverseHostChannel : UniverseChannel
{
    private readonly IUniverseHostReceiver _receiver;

    public UniverseHostChannel(Stream stream, IUniverseHostReceiver receiver) : base(stream)
    {
        _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
    }

    public void SendHello(RemoteHello hello)
    {
        lock (WriteGate)
        {
            var writer = StartMessage();
            writer.Write(hello.ProcessId);
            writer.Write(hello.DeviceUuid.ToByteArray());
            FinishMessage(UniverseMessageKind.Hello);
        }
    }

    public void SendSurface(RemoteSurface surface)
    {
        lock (WriteGate)
        {
            var writer = StartMessage();
            writer.Write(surface.Width);
            writer.Write(surface.Height);
            writer.Write((int)surface.Format.Value);
            writer.Write((int)surface.HandleType);
            writer.Write(surface.Buffers.Length);
            foreach (var buffer in surface.Buffers)
            {
                writer.Write(buffer.MemoryHandle);
                writer.Write(buffer.AllocationSize);
                writer.Write(buffer.ProduceSemaphoreHandle);
                writer.Write(buffer.ConsumeSemaphoreHandle);
            }

            FinishMessage(UniverseMessageKind.Surface);
        }
    }

    public void SendFrameReady(ulong frame)
    {
        lock (WriteGate)
        {
            StartMessage().Write(frame);
            FinishMessage(UniverseMessageKind.FrameReady);
        }
    }

    public void SendStopped() => SendEmpty(UniverseMessageKind.Stopped);

    protected override void Receive(UniverseMessageKind kind, BinaryReader payload)
    {
        switch (kind)
        {
            case UniverseMessageKind.Resize:
                _receiver.Resize(payload.ReadUInt32(), payload.ReadUInt32(), payload.ReadSingle());
                break;
            case UniverseMessageKind.FrameAsked:
                _receiver.AskFrame(payload.ReadUInt64());
                break;
            case UniverseMessageKind.Pointer:
                _receiver.MovePointer(new Vector2F(payload.ReadSingle(), payload.ReadSingle()), payload.ReadBoolean());
                break;
            case UniverseMessageKind.Focus:
                _receiver.Focus(payload.ReadBoolean());
                break;
            case UniverseMessageKind.Keyboard:
                _receiver.TakeKeyboardInput(new KeyboardInput
                {
                    Key = (Keys)payload.ReadInt32(),
                    InputType = (InputType)payload.ReadInt32()
                });
                break;
            case UniverseMessageKind.Mouse:
                _receiver.TakeMouseInput(ReadMouseInput(payload));
                break;
            case UniverseMessageKind.Gesture:
                _receiver.TakeGestureInput(new GestureInput
                {
                    Translation = new Vector2F(payload.ReadSingle(), payload.ReadSingle()),
                    Scale = payload.ReadSingle(),
                    Rotation = payload.ReadSingle()
                });
                break;
            case UniverseMessageKind.Text:
                _receiver.TakeText(payload.ReadString());
                break;
            case UniverseMessageKind.Stop:
                _receiver.Stop();
                break;
        }
    }

    protected override void LoseOtherEnd() => _receiver.LoseViewer();

    private static MouseInput ReadMouseInput(BinaryReader payload)
    {
        var input = new MouseInput
        {
            Button = (MouseButton)payload.ReadInt32(),
            InputType = (InputType)payload.ReadInt32(),
            WheelDelta = payload.ReadInt32(),
            Delta = new Vector2F(payload.ReadSingle(), payload.ReadSingle()),
            ClickCount = payload.ReadInt32()
        };
        if (payload.ReadBoolean())
        {
            input.Position = new Vector2F(payload.ReadSingle(), payload.ReadSingle());
        }

        return input;
    }
}
