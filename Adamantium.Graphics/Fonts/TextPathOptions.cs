using System;

namespace Adamantium.Graphics.Fonts;

/// <summary>Where along its path a <see cref="TextPath"/> stands its text, as the text is read.</summary>
public enum TextPathAlignment
{
    /// <summary>From the start of the path.</summary>
    Start,

    /// <summary>About the middle of the path.</summary>
    Center,

    /// <summary>Up to the end of the path.</summary>
    End,
}

/// <summary>What a <see cref="TextPath"/> does with text longer than its path.</summary>
public enum TextPathOverflow
{
    /// <summary>Draws the letters that stand on the path, as SVG's <c>textPath</c>: one whose middle falls past an end
    /// of an open path is left out.</summary>
    Clip,

    /// <summary>Draws every letter: one past an end of an open path stands on its going on straight, the way its end
    /// runs.</summary>
    Extend,

    /// <summary>Cuts the text where it leaves an open path - at its start, its end or both, however long it is - with
    /// an ellipsis there, the text's attributes kept; text read left to right.</summary>
    Ellipsis,

    /// <summary>Draws the text smaller, as much as it takes to fit the path.</summary>
    Shrink,

    /// <summary>Draws all of the text or none of it - as a map's label, which would rather be left out than cut; it is
    /// also left out where the path turns between two letters by more than <see cref="TextPathOptions.MaxTurn"/>.</summary>
    Hide,
}

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
