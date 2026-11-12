using System;

namespace Adamantium.Engine.Snapshots;

/// <summary>Draws a model alone, whole in view, to a picture for its asset's thumbnail.</summary>
public interface IModelSnapshotService : IDisposable
{
    /// <summary>The width and height of a snapshot, in pixels.</summary>
    uint Size { get; }

    /// <summary>The model at <paramref name="assetPath"/> - an asset file or a source a reader takes - drawn in its bind
    /// pose from the front, above and to the right, filling the picture, over a transparent background, as PNG; null when
    /// it has nothing to draw. One snapshot at a time: a call made while another runs waits for it. Throws what loading
    /// the model throws.</summary>
    byte[] TakePng(string assetPath);
}
