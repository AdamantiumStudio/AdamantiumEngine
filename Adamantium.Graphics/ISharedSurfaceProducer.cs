using System;
using Adamantium.Graphics.Core;

namespace Adamantium.Graphics;

/// <summary>Sends an output's frames to an <see cref="ISharedSurfaceConsumer"/> through shared surfaces. Driven from the
/// thread that draws the output.</summary>
public interface ISharedSurfaceProducer : IDisposable
{
    /// <summary>Copies <paramref name="frame"/>, <paramref name="width"/> × <paramref name="height"/>, on
    /// <paramref name="device"/>'s queue after what drew it. <paramref name="inDevice"/>: the consumer draws with this
    /// device's own logical device, so the surfaces need no OS handles. Followed by <see cref="NoteSubmitted"/> once the
    /// device's frame is submitted.</summary>
    void Produce(IGraphicsDevice device, ITexture frame, uint width, uint height, bool inDevice);

    /// <summary>Tells the consumer of the frame <see cref="Produce"/> copied, once the device's frame is submitted.</summary>
    void NoteSubmitted();

    /// <summary>Whether the surfaces of a new size or a new ring wait to be handed to the consumer with the next frame -
    /// the frame that started them is in them, not shown yet.</summary>
    bool HandsOverWithNextFrame { get; }

    /// <summary>Waits until the consumer asks for a frame and has given back a surface for it, up to
    /// <paramref name="timeout"/>. Gives whether it did; if not, asks for a frame again on the consumer's next ask.</summary>
    bool WaitUntilShown(TimeSpan timeout);

    /// <summary>Lets the consumer go of the surfaces and frees them: <paramref name="retire"/> once the device's frames
    /// still in flight are done with them, else at once, for a device that is going away.</summary>
    void Release(bool retire);
}
