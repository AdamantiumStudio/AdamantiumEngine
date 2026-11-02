namespace Adamantium.Graphics.Fonts;

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
