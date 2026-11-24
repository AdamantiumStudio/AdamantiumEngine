using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Multiverse.Input;
using Adamantium.Multiverse.Remote;

namespace Adamantium.EngineTests.Remote;

/// <summary>A host's service that writes down what its viewer called, one line a call, and answers simply.</summary>
public class RecordingHostService : IUniverseHostService
{
    public BlockingCollection<string> Calls { get; } = new();

    public Task LaunchAsync(UniverseLaunch launch, CancellationToken cancellationToken)
    {
        if (!System.IO.File.Exists(launch.ScenePath))
        {
            throw new InvalidOperationException("No scene at " + launch.ScenePath);
        }

        Calls.Add($"Launch {System.IO.Path.GetExtension(launch.ScenePath)}");
        return Task.CompletedTask;
    }

    public void Resize(uint width, uint height, float pixelsPerPoint) =>
        Calls.Add(string.Create(CultureInfo.InvariantCulture, $"Resize {width}x{height} at {pixelsPerPoint}"));

    public void AskFrame(ulong frame) => Calls.Add($"AskFrame {frame}");

    public void AskNewSurface() => Calls.Add("AskNewSurface");

    public void DrawWhereItStands(bool heldAtAStatement) => Calls.Add($"DrawWhereItStands {heldAtAStatement}");

    public Task GoOnAsync(CancellationToken cancellationToken)
    {
        Calls.Add("GoOn");
        return Task.CompletedTask;
    }

    public void MovePointer(float x, float y, bool isOver) =>
        Calls.Add(string.Create(CultureInfo.InvariantCulture, $"MovePointer {x},{y} {isOver}"));

    public void Focus(bool hasKeyboard) => Calls.Add($"Focus {hasKeyboard}");

    public void TakeKeyboardInput(KeyboardInput input) => Calls.Add($"Keyboard {input.Key} {input.InputType}");

    public void TakeMouseInput(MouseInput input) => Calls.Add(string.Create(CultureInfo.InvariantCulture,
        $"Mouse {input.Button} {input.InputType} {input.WheelDelta} {input.ClickCount} {(input.Position is { } p ? $"{p.X},{p.Y}" : "none")}"));

    public void TakeGestureInput(GestureInput input) => Calls.Add(string.Create(CultureInfo.InvariantCulture,
        $"Gesture {input.Translation.X},{input.Translation.Y} {input.Scale} {input.Rotation}"));

    public void TakeText(string text) => Calls.Add($"Text {text}");

    /// <summary>Set, the scene is captured once it is done - or called off.</summary>
    public TaskCompletionSource<string> HeldScene { get; set; }

    public Task<string> CaptureSceneAsync(CancellationToken cancellationToken) =>
        HeldScene?.Task.WaitAsync(cancellationToken) ?? Task.FromResult("<Scene />");

    public Task<byte[]> CaptureFrameAsync(CancellationToken cancellationToken) => Task.FromResult(new byte[] { 1, 2, 3 });

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Calls.Add("Stop");
        return Task.CompletedTask;
    }
}
