using Adamantium.Multiverse.Input;
using StreamJsonRpc;

namespace Adamantium.Multiverse.Remote;

/// <summary>What a universe host serves its viewer: the game launched, its frames paced, the user's input and size
/// given, the scene and the frame read back, the universe stopped. Methods that return nothing are not waited for.</summary>
[JsonRpcContract]
public partial interface IUniverseHostService
{
    /// <summary>Loads and starts the game; done once it plays. A scene or code it cannot load fails it, with the reason;
    /// a stop before it plays cancels it. Once the universe stopped, or the launch failed, the viewer lets go of the
    /// connection, and the host's process ends.</summary>
    Task LaunchAsync(UniverseLaunch launch, CancellationToken cancellationToken);

    /// <summary>Draws at <paramref name="width"/> × <paramref name="height"/> pixels, <paramref name="pixelsPerPoint"/>
    /// to a point.</summary>
    void Resize(uint width, uint height, float pixelsPerPoint);

    /// <summary>The viewer asks for frame <paramref name="frame"/>: it has shown the ones before.</summary>
    void AskFrame(ulong frame);

    /// <summary>Puts the pointer at <paramref name="x"/>, <paramref name="y"/> in the surface's pixels;
    /// <paramref name="isOver"/> says whether it is over the view.</summary>
    void MovePointer(float x, float y, bool isOver);

    /// <summary>Gives or takes the keyboard.</summary>
    void Focus(bool hasKeyboard);

    void TakeKeyboardInput(KeyboardInput input);

    void TakeMouseInput(MouseInput input);

    void TakeGestureInput(GestureInput input);

    void TakeText(string text);

    /// <summary>The scene as the game has it now, as a scene file's text.</summary>
    Task<string> CaptureSceneAsync(CancellationToken cancellationToken);

    /// <summary>The last frame drawn, as a PNG.</summary>
    Task<byte[]> CaptureFrameAsync(CancellationToken cancellationToken);

    /// <summary>Stops the universe; done once it stopped.</summary>
    Task StopAsync(CancellationToken cancellationToken);
}
