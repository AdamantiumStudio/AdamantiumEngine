using Adamantium.Multiverse.Input;
using MessagePack;

namespace Adamantium.Multiverse.Remote;

/// <summary>The host's service as its viewer calls it: each call a message on the link.</summary>
internal sealed class UniverseHostServiceProxy(RemoteLink link) : IUniverseHostService
{
    public Task LaunchAsync(UniverseLaunch launch, CancellationToken cancellationToken) =>
        link.CallAsync<object>((int)HostMethod.Launch, (ref MessagePackWriter writer) => Write(ref writer, launch),
            cancellationToken);

    public void Resize(uint width, uint height, float pixelsPerPoint) =>
        link.Tell((int)HostMethod.Resize, (ref MessagePackWriter writer) =>
        {
            writer.Write(width);
            writer.Write(height);
            writer.Write(pixelsPerPoint);
        });

    public void AskFrame(ulong frame) =>
        link.Tell((int)HostMethod.AskFrame, (ref MessagePackWriter writer) => writer.Write(frame));

    public void AskNewSurface() => link.Tell((int)HostMethod.AskNewSurface, null);

    public void DrawWhereItStands() => link.Tell((int)HostMethod.DrawWhereItStands, null);

    public Task GoOnAsync(CancellationToken cancellationToken) =>
        link.CallAsync<object>((int)HostMethod.GoOn, null, cancellationToken);

    public void MovePointer(float x, float y, bool isOver) =>
        link.Tell((int)HostMethod.MovePointer, (ref MessagePackWriter writer) =>
        {
            writer.Write(x);
            writer.Write(y);
            writer.Write(isOver);
        });

    public void Focus(bool hasKeyboard) =>
        link.Tell((int)HostMethod.Focus, (ref MessagePackWriter writer) => writer.Write(hasKeyboard));

    public void TakeKeyboardInput(KeyboardInput input) =>
        link.Tell((int)HostMethod.TakeKeyboardInput, (ref MessagePackWriter writer) => Write(ref writer, input));

    public void TakeMouseInput(MouseInput input) =>
        link.Tell((int)HostMethod.TakeMouseInput, (ref MessagePackWriter writer) => Write(ref writer, input));

    public void TakeGestureInput(GestureInput input) =>
        link.Tell((int)HostMethod.TakeGestureInput, (ref MessagePackWriter writer) => Write(ref writer, input));

    public void TakeText(string text) =>
        link.Tell((int)HostMethod.TakeText, (ref MessagePackWriter writer) => writer.Write(text));

    public Task<string> CaptureSceneAsync(CancellationToken cancellationToken) =>
        link.CallAsync<string>((int)HostMethod.CaptureScene, null, cancellationToken);

    public Task<byte[]> CaptureFrameAsync(CancellationToken cancellationToken) =>
        link.CallAsync<byte[]>((int)HostMethod.CaptureFrame, null, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) =>
        link.CallAsync<object>((int)HostMethod.Stop, null, cancellationToken);

    private static void Write<T>(ref MessagePackWriter writer, T value) =>
        MessagePackSerializer.Serialize(ref writer, value, RemoteLink.Options);
}
