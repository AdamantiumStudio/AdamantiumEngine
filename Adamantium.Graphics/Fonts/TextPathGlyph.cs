using Adamantium.Mathematics;

namespace Adamantium.Graphics.Fonts;

/// <summary>A letter of a <see cref="TextPath"/>: its cluster, where its middle stands and the way it is turned, and
/// whether it is drawn.</summary>
public readonly record struct TextPathGlyph(GlyphCluster Cluster, Vector2<double> Point, double Angle, bool IsShown);
