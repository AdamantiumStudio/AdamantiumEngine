using System;
using System.Globalization;

namespace Adamantium.Core.Fonts;

/// <summary>
/// How wide a typeface is, as a percentage of its normal width: 50 for ultra-condensed up to 200 for ultra-expanded, as
/// CSS counts it and as the <c>wdth</c> axis of a variable font takes it. Parsed from a name (<c>Condensed</c>) or a
/// percentage (<c>75%</c>).
/// </summary>
public readonly struct FontStretch : IEquatable<FontStretch>, IComparable<FontStretch>
{
    private static readonly double[] WidthClassPercents = [50, 62.5, 75, 87.5, 100, 112.5, 125, 150, 200];

    private readonly double _percent;

    public FontStretch(double percent)
    {
        if (percent is <= 0 or > 1000 || double.IsNaN(percent))
        {
            throw new ArgumentOutOfRangeException(nameof(percent), percent, "A font stretch is a percentage above 0.");
        }

        _percent = percent;
    }

    public static readonly FontStretch UltraCondensed = new(50);

    public static readonly FontStretch ExtraCondensed = new(62.5);

    public static readonly FontStretch Condensed = new(75);

    public static readonly FontStretch SemiCondensed = new(87.5);

    public static readonly FontStretch Normal = new(100);

    public static readonly FontStretch SemiExpanded = new(112.5);

    public static readonly FontStretch Expanded = new(125);

    public static readonly FontStretch ExtraExpanded = new(150);

    public static readonly FontStretch UltraExpanded = new(200);

    /// <summary>The width as a percentage of normal; a stretch never set reads as 100.</summary>
    public double Percent => _percent == 0 ? 100 : _percent;

    /// <summary>The stretch an OpenType width class (<c>OS/2</c> <c>usWidthClass</c>, 1 to 9) stands for.</summary>
    public static FontStretch FromWidthClass(int widthClass)
    {
        return new FontStretch(WidthClassPercents[Math.Max(1, Math.Min(9, widthClass)) - 1]);
    }

    public static FontStretch Parse(string text)
    {
        if (!TryParse(text, out var stretch))
        {
            throw new FormatException(
                $"'{text}' is not a font stretch: expected a name such as Condensed or a percentage such as 75%.");
        }

        return stretch;
    }

    public static bool TryParse(string text, out FontStretch stretch)
    {
        stretch = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        var number = text.EndsWith("%", StringComparison.Ordinal) ? text.Substring(0, text.Length - 1) : text;
        if (double.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var percent))
        {
            if (percent is <= 0 or > 1000)
            {
                return false;
            }

            stretch = new FontStretch(percent);
            return true;
        }

        var value = text.ToLowerInvariant() switch
        {
            "ultracondensed" => 50,
            "extracondensed" => 62.5,
            "condensed" => 75,
            "semicondensed" => 87.5,
            "normal" => 100,
            "semiexpanded" => 112.5,
            "expanded" => 125,
            "extraexpanded" => 150,
            "ultraexpanded" => 200,
            _ => 0,
        };
        if (value == 0)
        {
            return false;
        }

        stretch = new FontStretch(value);
        return true;
    }

    public bool Equals(FontStretch other) => Percent.Equals(other.Percent);

    public override bool Equals(object obj) => obj is FontStretch other && Equals(other);

    public override int GetHashCode() => Percent.GetHashCode();

    public int CompareTo(FontStretch other) => Percent.CompareTo(other.Percent);

    public static bool operator ==(FontStretch left, FontStretch right) => left.Equals(right);

    public static bool operator !=(FontStretch left, FontStretch right) => !left.Equals(right);

    public override string ToString()
    {
        return Percent switch
        {
            50 => nameof(UltraCondensed),
            62.5 => nameof(ExtraCondensed),
            75 => nameof(Condensed),
            87.5 => nameof(SemiCondensed),
            100 => nameof(Normal),
            112.5 => nameof(SemiExpanded),
            125 => nameof(Expanded),
            150 => nameof(ExtraExpanded),
            200 => nameof(UltraExpanded),
            _ => Percent.ToString(CultureInfo.InvariantCulture) + "%",
        };
    }
}
