using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A text of a markup tessellation: the string, the font key of the globals it is written in (-1 for none
/// given), its width and height in the model's units, and the point it starts at.</summary>
internal sealed class PrcMarkupText
{
    internal string Text { get; init; }

    internal int FontIndex { get; init; }

    internal double Width { get; init; }

    internal double Height { get; init; }

    internal Vector3<double> Position { get; init; }
}
