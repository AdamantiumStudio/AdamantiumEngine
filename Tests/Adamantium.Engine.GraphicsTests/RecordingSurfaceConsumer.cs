using System.Collections.Generic;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>A consumer that only notes what the producer told it, in order.</summary>
public class RecordingSurfaceConsumer : ISharedSurfaceConsumer
{
    public List<SharedSurfaceDescriptor> Bound { get; } = [];

    public List<SharedSurfaceDescriptor> Unbound { get; } = [];

    public int FramesSubmitted { get; private set; }

    public int FramesAsked { get; private set; }

    public void Bind(SharedSurfaceDescriptor descriptor) => Bound.Add(descriptor);

    public void Unbind(SharedSurfaceDescriptor descriptor) => Unbound.Add(descriptor);

    public void NoteFrameSubmitted() => FramesSubmitted++;

    public void AskFrame() => FramesAsked++;
}
