using Adamantium.Mathematics;

namespace Adamantium.Graphics.Fonts;

/// <summary>A cluster of text put on a path: where its middle stands and the way the path runs there, in radians from
/// the X axis toward Y.</summary>
public readonly record struct GlyphOnPath(Vector2<double> Point, double Angle);
