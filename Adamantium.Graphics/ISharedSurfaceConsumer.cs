using Adamantium.Graphics.Core;

namespace Adamantium.Graphics;

/// <summary>Whoever shows the frames a <see cref="SharedSurfaceProducer"/> makes: a panel of the UI in this process, a
/// channel to another process.</summary>
public interface ISharedSurfaceConsumer
{
    /// <summary>Takes the frames of <paramref name="descriptor"/> from now on; it already holds one.</summary>
    void Bind(SharedSurfaceDescriptor descriptor);

    /// <summary>Lets go of <paramref name="descriptor"/>: it is not written again.</summary>
    void Unbind(SharedSurfaceDescriptor descriptor);

    /// <summary>A frame was submitted; it may still be drawing.</summary>
    void NoteFrameSubmitted();

    /// <summary>Asks the producer's owner for another frame.</summary>
    void AskFrame();
}
