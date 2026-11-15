namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A font key of the globals' markup fonts (FontKeysSameFont): the font's family name, a size and its
/// attributes - bold, italic, underlined and the rest.</summary>
internal sealed class PrcFont
{
    internal string Name { get; init; }

    internal int Size { get; init; }

    internal byte Attributes { get; init; }
}
