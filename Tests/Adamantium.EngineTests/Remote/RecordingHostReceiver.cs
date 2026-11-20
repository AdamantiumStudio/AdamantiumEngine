using System;
using System.Collections.Concurrent;
using System.Globalization;
using Adamantium.Mathematics;
using Adamantium.Multiverse.Input;
using Adamantium.Multiverse.Remote;

namespace Adamantium.EngineTests.Remote;

/// <summary>A host end that writes down what its viewer told it, one line a message, in order.</summary>
public class RecordingHostReceiver : IUniverseHostReceiver
{
    public BlockingCollection<string> Messages { get; } = new();

    public void Resize(uint width, uint height, float pixelsPerPoint) =>
        Messages.Add(string.Create(CultureInfo.InvariantCulture, $"Resize {width}x{height} at {pixelsPerPoint}"));

    public void AskFrame(ulong frame) => Messages.Add($"AskFrame {frame}");

    public void MovePointer(Vector2F position, bool isOver) =>
        Messages.Add(string.Create(CultureInfo.InvariantCulture, $"MovePointer {position.X},{position.Y} {isOver}"));

    public void Focus(bool hasKeyboard) => Messages.Add($"Focus {hasKeyboard}");

    public void TakeKeyboardInput(KeyboardInput input) => Messages.Add($"Keyboard {input.Key} {input.InputType}");

    public void TakeMouseInput(MouseInput input) => Messages.Add(string.Create(CultureInfo.InvariantCulture,
        $"Mouse {input.Button} {input.InputType} {input.WheelDelta} {input.Delta.X},{input.Delta.Y} {input.ClickCount} {(input.Position is { } p ? $"{p.X},{p.Y}" : "none")}"));

    public void TakeGestureInput(GestureInput input) => Messages.Add(string.Create(CultureInfo.InvariantCulture,
        $"Gesture {input.Translation.X},{input.Translation.Y} {input.Scale} {input.Rotation}"));

    public void TakeText(string text) => Messages.Add($"Text {text}");

    public void Stop() => Messages.Add("Stop");

    public void LoseViewer() => Messages.Add("LoseViewer");
}
