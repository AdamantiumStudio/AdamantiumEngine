using System;

namespace Adamantium.Graphics.Fonts;

/// <summary>How a <see cref="TextPath"/> lays its text along the path.</summary>
public sealed record TextPathOptions
{
    /// <summary>Where along the path the text stands; <see cref="TextPathAlignment.Start"/> by default.</summary>
    public TextPathAlignment Alignment { get; init; } = TextPathAlignment.Start;

    /// <summary>How far, in the path's units, the text is moved on from where <see cref="Alignment"/> stands it; back
    /// for a value below nothing.</summary>
    public double StartOffset { get; init; }

    /// <summary>How far, in the path's units, the text's baseline stands above the path as it is read; below it for a
    /// value below nothing.</summary>
    public double AcrossOffset { get; init; }

    /// <summary>Whether an open path that ends left of where it starts is read from its end, so the text does not stand
    /// on its head; true by default. A closed path is read the way it goes.</summary>
    public bool KeepUpright { get; init; } = true;

    /// <summary>The most, in radians, a path may turn between two neighboring letters, every bend between them added
    /// up, for <see cref="TextPathOverflow.Hide"/> to draw the text; π by default.</summary>
    public double MaxTurn { get; init; } = Math.PI;

    /// <summary>What is done with text longer than the path; <see cref="TextPathOverflow.Clip"/> by default.</summary>
    public TextPathOverflow Overflow { get; init; } = TextPathOverflow.Clip;

    /// <summary>How many of the path's units a unit of the laid out text takes: the text drawn larger or smaller than it
    /// was laid out; 1 by default.</summary>
    public double Scale { get; init; } = 1;
}
