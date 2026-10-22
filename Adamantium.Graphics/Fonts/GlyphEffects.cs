using Adamantium.Mathematics;

namespace Adamantium.Graphics.Fonts;

/// <summary>A line drawn round each glyph's outline, outside it, under the glyph: Word's text outline. Its
/// <see cref="Width"/> in the layout's units reaches about a tenth of an em at most, as far as the glyph's distance
/// field goes.</summary>
public sealed record GlyphOutline(Color Color, double Width);

/// <summary>A copy of each glyph under it, moved and softened: a text shadow. Distances in the layout's units; its
/// <see cref="Blur"/> reaches about a tenth of an em at most, as far as the glyph's distance field goes.</summary>
public sealed record GlyphShadow(Color Color, double OffsetX, double OffsetY, double Blur);

/// <summary>A soft band of color round each glyph, under it: a text glow. Its <see cref="Radius"/> in the layout's
/// units reaches about a tenth of an em at most, as far as the glyph's distance field goes.</summary>
public sealed record GlyphGlow(Color Color, double Radius);

/// <summary>Each glyph again, upside down below its line, fading away from it: a text reflection, as Word draws it
/// under each line of a paragraph, over the line below; a drop cap's below the last line it spans; none in vertical
/// text. <see cref="Opacity"/> is how strong it starts, 0 to 1; <see cref="Distance"/> how far below the line it stands,
/// in the layout's units; <see cref="Size"/> how much of the glyph shows before it has faded, 0 to 1.</summary>
public sealed record GlyphReflection(double Opacity, double Distance, double Size);
