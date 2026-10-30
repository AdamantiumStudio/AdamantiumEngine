using System;

namespace Adamantium.Graphics.Core.Presentation;

/// <summary>Takes the frames a <see cref="PresenterType.Readback"/> presenter reads back, to put them on screen itself.</summary>
public interface IFrameSink
{
    /// <summary>Shows a frame: <paramref name="height"/> rows of <paramref name="stride"/> bytes from
    /// <paramref name="pixels"/>, top row first, each pixel B8G8R8A8 as drawn. The memory is the presenter's and is
    /// reused for the next frame, so it is read before this returns.</summary>
    void Show(IntPtr pixels, uint width, uint height, uint stride);
}
