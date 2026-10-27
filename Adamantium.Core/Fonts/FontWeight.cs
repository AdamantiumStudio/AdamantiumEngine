using System;
using System.Globalization;

namespace Adamantium.Core.Fonts;

/// <summary>
/// How heavy a typeface is: a number from 1 to 1000, 400 for regular text and 700 for bold, as OpenType (<c>OS/2</c>
/// <c>usWeightClass</c>) and CSS count it. Parsed from a name (<c>SemiBold</c>) or a number (<c>650</c>).
/// </summary>
public readonly struct FontWeight : IEquatable<FontWeight>, IComparable<FontWeight>
{
    private readonly int _value;

    public FontWeight(int value)
    {
        if (value is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A font weight is from 1 to 1000.");
        }

        _value = value;
    }

    public static readonly FontWeight Thin = new(100);

    public static readonly FontWeight ExtraLight = new(200);

    public static readonly FontWeight Light = new(300);

    public static readonly FontWeight Normal = new(400);

    public static readonly FontWeight Medium = new(500);

    public static readonly FontWeight SemiBold = new(600);

    public static readonly FontWeight Bold = new(700);

    public static readonly FontWeight ExtraBold = new(800);

    public static readonly FontWeight Black = new(900);

    /// <summary>From 1 to 1000; a weight never set reads as 400.</summary>
    public int Value => _value == 0 ? 400 : _value;

    public static FontWeight Parse(string text)
    {
        if (!TryParse(text, out var weight))
        {
            throw new FormatException(
                $"'{text}' is not a font weight: expected a name such as SemiBold or a number from 1 to 1000.");
        }

        return weight;
    }

    public static bool TryParse(string text, out FontWeight weight)
    {
        weight = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            if (number is < 1 or > 1000)
            {
                return false;
            }

            weight = new FontWeight(number);
            return true;
        }

        var value = text.ToLowerInvariant() switch
        {
            "thin" => 100,
            "extralight" => 200,
            "light" => 300,
            "normal" or "regular" => 400,
            "medium" => 500,
            "semibold" => 600,
            "bold" => 700,
            "extrabold" => 800,
            "black" => 900,
            _ => 0,
        };
        if (value == 0)
        {
            return false;
        }

        weight = new FontWeight(value);
        return true;
    }

    public bool Equals(FontWeight other) => Value == other.Value;

    public override bool Equals(object obj) => obj is FontWeight other && Equals(other);

    public override int GetHashCode() => Value;

    public int CompareTo(FontWeight other) => Value.CompareTo(other.Value);

    public static bool operator ==(FontWeight left, FontWeight right) => left.Equals(right);

    public static bool operator !=(FontWeight left, FontWeight right) => !left.Equals(right);

    public override string ToString()
    {
        return Value switch
        {
            100 => nameof(Thin),
            200 => nameof(ExtraLight),
            300 => nameof(Light),
            400 => nameof(Normal),
            500 => nameof(Medium),
            600 => nameof(SemiBold),
            700 => nameof(Bold),
            800 => nameof(ExtraBold),
            900 => nameof(Black),
            _ => Value.ToString(CultureInfo.InvariantCulture),
        };
    }
}
