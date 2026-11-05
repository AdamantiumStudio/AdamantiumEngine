namespace Adamantium.Graphics.Fonts;

/// <summary>How a paragraph's border line is drawn, as Word's line styles and CSS's border styles.</summary>
public enum ParagraphBorderStyle
{
    /// <summary>One unbroken line.</summary>
    Solid,

    /// <summary>Dots as long as the line is thick, as far apart.</summary>
    Dotted,

    /// <summary>Dashes three times as long as the line is thick, two times as far apart.</summary>
    Dashed,

    /// <summary>Two lines, each as thick as the line's width, as far apart: three times its width, as Word draws it.</summary>
    Double,
}
