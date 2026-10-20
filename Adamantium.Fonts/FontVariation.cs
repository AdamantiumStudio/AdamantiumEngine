using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Adamantium.Fonts;

/// <summary>A value for one axis of a variable font, as CSS font-variation-settings writes it: wght=650.</summary>
public readonly struct FontVariation : IEquatable<FontVariation>
{
    public FontVariation(string tag, float value)
    {
        Tag = tag;
        Value = value;
    }

    /// <summary>The axis tag: "wght", "wdth", "opsz"...</summary>
    public string Tag { get; }

    public float Value { get; }

    /// <summary>The axis values a weight and a width set on a variable font, as CSS sets them: 'wght' to the weight,
    /// 'wdth' to the width in percent.</summary>
    public static FontVariation[] For(FontWeight weight, FontStretch stretch) =>
        [new FontVariation("wght", weight.Value), new FontVariation("wdth", (float)stretch.Percent)];

    /// <summary>The axis values a weight, width and style ask of <paramref name="font"/>, as CSS Fonts 4 sets them: an
    /// italic turns <c>ital</c> on, or slants by <c>slnt</c> when the font has no <c>ital</c>; an oblique slants by
    /// <c>slnt</c>, 14 degrees or as far as the axis goes.</summary>
    public static FontVariation[] For(FontWeight weight, FontStretch stretch, FontStyle style, IFont font)
    {
        var variations = new List<FontVariation>(For(weight, stretch));
        var hasItalic = font.Axes.Any(axis => axis.Tag == "ital");
        if (hasItalic)
        {
            variations.Add(new FontVariation("ital", style == FontStyle.Italic ? 1 : 0));
        }

        if (font.Axes.FirstOrDefault(axis => axis.Tag == "slnt") is { } slant)
        {
            var slants = style == FontStyle.Oblique || (style == FontStyle.Italic && !hasItalic);
            variations.Add(new FontVariation("slnt", slants ? Math.Max(slant.MinValue, ObliqueAngle) : 0));
        }

        return variations.ToArray();
    }

    /// <summary>Parses a comma-separated list, such as <c>wght=650, "wdth"=87.5, opsz=auto</c>: an axis tag of four
    /// characters, quoted or not - a shorter one written with the spaces OpenType pads it with, <c>TS  =5</c> - and its
    /// finite number. <c>opsz=auto</c> sets none, leaving the optical size to follow the text's size; blank entries are
    /// passed over.</summary>
    /// <exception cref="FormatException">An entry is not a tag and its number.</exception>
    public static IReadOnlyList<FontVariation> ParseList(string text)
    {
        var variations = new List<FontVariation>();
        foreach (var entry in (text ?? string.Empty).Split(','))
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            var parts = entry.Split('=');
            var tag = parts[0].TrimStart();
            if (tag.Length > 0 && tag[0] is '"' or '\'')
            {
                var quoted = tag.TrimEnd();
                if (quoted.Length >= 2 && quoted[quoted.Length - 1] == quoted[0])
                {
                    tag = quoted.Substring(1, quoted.Length - 2);
                }
            }
            else if (tag.Length > 4)
            {
                tag = tag.TrimEnd();
            }

            if (parts.Length != 2 || tag.Length != 4 || tag[0] == ' ')
            {
                throw new FormatException($"'{entry.Trim()}' is not an axis and its value, as wght=650");
            }

            var value = parts[1].Trim();
            if (tag == "opsz" && value == "auto")
            {
                continue;
            }

            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                || float.IsNaN(number) || float.IsInfinity(number))
            {
                throw new FormatException($"'{value}' is not a value for the axis {tag}");
            }

            variations.Add(new FontVariation(tag, number));
        }

        return variations;
    }

    private const float ObliqueAngle = -14;

    public bool Equals(FontVariation other) => Tag == other.Tag && Value.Equals(other.Value);

    public override bool Equals(object obj) => obj is FontVariation other && Equals(other);

    public override int GetHashCode() => ((Tag?.GetHashCode() ?? 0) * 397) ^ Value.GetHashCode();

    public override string ToString() => $"{Tag}={Value}";
}
