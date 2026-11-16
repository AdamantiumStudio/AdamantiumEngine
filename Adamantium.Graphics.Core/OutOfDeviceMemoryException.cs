namespace Adamantium.Graphics.Core;

/// <summary>The GPU refused memory for a buffer or an image: its memory is full - often held by another application, a
/// game say. Nothing is wrong with what asked; asked again once memory is freed, it can succeed.</summary>
public class OutOfDeviceMemoryException : GraphicsEngineException
{
    public OutOfDeviceMemoryException(string message) : base(message)
    {
    }
}
