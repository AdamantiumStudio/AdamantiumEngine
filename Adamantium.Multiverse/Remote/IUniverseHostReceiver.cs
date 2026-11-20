using Adamantium.Mathematics;
using Adamantium.Multiverse.Input;

namespace Adamantium.Multiverse.Remote;

/// <summary>What a universe host is told by its viewer, on the channel's reading thread.</summary>
public interface IUniverseHostReceiver
{
    /// <summary>Draws at <paramref name="width"/> × <paramref name="height"/> pixels, <paramref name="pixelsPerPoint"/>
    /// to a point.</summary>
    void Resize(uint width, uint height, float pixelsPerPoint);

    /// <summary>The viewer asks for frame <paramref name="frame"/>: it has shown the ones before.</summary>
    void AskFrame(ulong frame);

    /// <summary>Puts the pointer at <paramref name="position"/>, in the surface's pixels; <paramref name="isOver"/> says
    /// whether it is over the view.</summary>
    void MovePointer(Vector2F position, bool isOver);

    /// <summary>Gives or takes the keyboard.</summary>
    void Focus(bool hasKeyboard);

    void TakeKeyboardInput(KeyboardInput input);

    void TakeMouseInput(MouseInput input);

    void TakeGestureInput(GestureInput input);

    void TakeText(string text);

    /// <summary>Stops the universe.</summary>
    void Stop();

    /// <summary>The viewer is gone: the channel is closed.</summary>
    void LoseViewer();
}
