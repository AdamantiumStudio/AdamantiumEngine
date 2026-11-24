namespace Adamantium.Multiverse.Remote;

/// <summary>The methods of <see cref="IUniverseHostService"/>, as a message names them.</summary>
internal enum HostMethod
{
    Launch,
    Resize,
    AskFrame,
    AskNewSurface,
    DrawWhereItStands,
    MovePointer,
    Focus,
    TakeKeyboardInput,
    TakeMouseInput,
    TakeGestureInput,
    TakeText,
    CaptureScene,
    CaptureFrame,
    Stop
}
