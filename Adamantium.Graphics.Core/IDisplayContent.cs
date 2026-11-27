namespace Adamantium.Graphics.Core;

/// <summary>
/// An interface for a drawable component that is called to output rendered content to the screen.
/// </summary>
public interface IDisplayContent
{
    bool CanDisplayContent { get; }
    void Present();
}