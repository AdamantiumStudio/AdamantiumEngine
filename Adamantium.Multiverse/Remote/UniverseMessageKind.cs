namespace Adamantium.Multiverse.Remote;

/// <summary>What a message between a universe host and its viewer is.</summary>
public enum UniverseMessageKind : byte
{
    Hello = 1,
    Surface = 2,
    FrameReady = 3,
    Stopped = 4,
    Resize = 20,
    FrameAsked = 21,
    Pointer = 22,
    Focus = 23,
    Keyboard = 24,
    Mouse = 25,
    Gesture = 26,
    Text = 27,
    Stop = 28
}
