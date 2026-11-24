using Adamantium.Multiverse.Input;

namespace Adamantium.Multiverse.Remote;

/// <summary>What a universe host serves its viewer: the game launched, its frames paced, the user's input and size
/// given, the scene and the frame read back, the universe stopped. Methods that return nothing are not waited for; a
/// call that fails in the host throws <see cref="UniverseHostException"/> with its reason.</summary>
public interface IUniverseHostService
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

    /// <summary>The viewer asks for surfaces of the same size it has not imported yet - the ones it has were imported
    /// by a view it left - and is shown them as any new surface.</summary>
    void AskNewSurface();

    /// <summary>Draws the world as it stands, at once, with no step of the game: for a game a debugger holds, whose own
    /// thread cannot draw - at each stop, its view to show where it stands. Nothing for a game not launched debugged
    /// (<see cref="UniverseLaunch.IsDebugged"/>).</summary>
    void DrawWhereItStands();

    /// <summary>The game is about to be let go on from a stop: it draws where it stands no more; done once that drawing
    /// is over, so it never meets a step of the game.</summary>
    Task GoOnAsync(CancellationToken cancellationToken);

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
