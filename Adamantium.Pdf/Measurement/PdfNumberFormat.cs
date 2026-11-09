using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Measurement;

/// <summary>One unit of a measurement (ISO 32000-2, 12.9.2, number format dictionary): its label, its factor from the
/// unit before it, and how a value in it is written - decimals or a fraction, separators, the label before or after.
/// </summary>
public sealed class PdfNumberFormat
{
    private const string Type = "NumberFormat";

    /// <summary>A unit labeled <paramref name="unit"/>, <paramref name="factor"/> of it to one of the unit before.
    /// </summary>
    public PdfNumberFormat(string unit, double factor)
    {
        Unit = unit ?? "";
        Factor = factor;
    }

    /// <summary>The unit's label (<c>U</c>).</summary>
    public string Unit { get; }

    /// <summary>How many of this unit make one of the unit before (<c>C</c>).</summary>
    public double Factor { get; }

    /// <summary>How the last unit's value is written (<c>F</c>): decimals, a fraction, rounded or truncated to whole.
    /// </summary>
    public PdfNumberStyle Style { get; init; } = PdfNumberStyle.Decimal;

    /// <summary>For decimals, a power of ten - 100 for two places; for a fraction, its denominator (<c>D</c>).</summary>
    public int Precision { get; init; } = 100;

    /// <summary>Whether a fraction keeps <see cref="Precision"/> as its denominator rather than reducing it
    /// (<c>FD</c>).</summary>
    public bool FixedDenominator { get; init; }

    /// <summary>What separates thousands (<c>RT</c>).</summary>
    public string ThousandsSeparator { get; init; } = ",";

    /// <summary>What separates the decimals (<c>RD</c>).</summary>
    public string DecimalSeparator { get; init; } = ".";

    /// <summary>What stands between the label and a value it comes before (<c>PS</c>).</summary>
    public string PrefixSpacing { get; init; } = " ";

    /// <summary>What stands between a value and the label after it (<c>SS</c>).</summary>
    public string SuffixSpacing { get; init; } = " ";

    /// <summary>Whether the label comes before the value (<c>O</c> = <c>/P</c>) rather than after.</summary>
    public bool LabelFirst { get; init; }

    /// <summary>The number format <paramref name="value"/> describes, or null when it is none.</summary>
    public static PdfNumberFormat From(PdfObject value, IPdfObjectResolver resolver)
    {
        if (resolver.Resolve(value) is not PdfDictionary dictionary)
        {
            return null;
        }

        var factor = PdfValues.Number(dictionary["C"], resolver, double.NaN);
        if (!double.IsFinite(factor))
        {
            return null;
        }

        return new PdfNumberFormat(Text(dictionary["U"], resolver) ?? "", factor)
        {
            Style = (resolver.Resolve(dictionary["F"]) as PdfName)?.Value switch
            {
                "F" => PdfNumberStyle.Fraction,
                "R" => PdfNumberStyle.Round,
                "T" => PdfNumberStyle.Truncate,
                _ => PdfNumberStyle.Decimal,
            },
            Precision = Math.Max(1, PdfValues.Integer(dictionary["D"], resolver, 100)),
            FixedDenominator = resolver.Resolve(dictionary["FD"]) is PdfBoolean { Value: true },
            ThousandsSeparator = Text(dictionary["RT"], resolver) ?? ",",
            DecimalSeparator = Text(dictionary["RD"], resolver) ?? ".",
            PrefixSpacing = Text(dictionary["PS"], resolver) ?? " ",
            SuffixSpacing = Text(dictionary["SS"], resolver) ?? " ",
            LabelFirst = (resolver.Resolve(dictionary["O"]) as PdfName)?.Value == "P",
        };
    }

    /// <summary>The number format dictionary of this unit.</summary>
    public PdfDictionary ToDictionary()
    {
        var dictionary = new PdfDictionary();
        dictionary.Set("Type", new PdfName(Type));
        dictionary.Set("U", PdfString.FromText(Unit));
        dictionary.Set("C", new PdfReal(Factor));
        if (Style != PdfNumberStyle.Decimal)
        {
            dictionary.Set("F", new PdfName(Style switch
            {
                PdfNumberStyle.Fraction => "F",
                PdfNumberStyle.Round => "R",
                _ => "T",
            }));
        }

        dictionary.Set("D", new PdfInteger(Precision));
        if (FixedDenominator)
        {
            dictionary.Set("FD", PdfBoolean.True);
        }

        if (ThousandsSeparator != ",")
        {
            dictionary.Set("RT", PdfString.FromText(ThousandsSeparator));
        }

        if (DecimalSeparator != ".")
        {
            dictionary.Set("RD", PdfString.FromText(DecimalSeparator));
        }

        if (PrefixSpacing != " ")
        {
            dictionary.Set("PS", PdfString.FromText(PrefixSpacing));
        }

        if (SuffixSpacing != " ")
        {
            dictionary.Set("SS", PdfString.FromText(SuffixSpacing));
        }

        if (LabelFirst)
        {
            dictionary.Set("O", new PdfName("P"));
        }

        return dictionary;
    }

    /// <summary><paramref name="value"/>, already in this unit, written with its label: as a whole number when
    /// <paramref name="whole"/>, as this unit's style says otherwise.</summary>
    public string Format(double value, bool whole)
    {
        var magnitude = Math.Abs(value);
        var number = whole
            ? Group(Math.Floor(magnitude).ToString("0", CultureInfo.InvariantCulture))
            : Last(RoundLast(magnitude));
        var sign = value < 0 && number.Any(c => c is >= '1' and <= '9') ? "-" : "";
        return LabelFirst ? Unit + PrefixSpacing + sign + number : sign + number + SuffixSpacing + Unit;
    }

    /// <summary><paramref name="value"/>, not negative, rounded as this unit writes it - to its decimals, its
    /// fraction, or a whole number.</summary>
    internal double RoundLast(double value) => Style switch
    {
        PdfNumberStyle.Round => Math.Round(value, MidpointRounding.AwayFromZero),
        PdfNumberStyle.Truncate => Math.Truncate(value),
        PdfNumberStyle.Fraction => Math.Round(value * Precision, MidpointRounding.AwayFromZero) / Precision,
        _ => Math.Round(value, Places, MidpointRounding.AwayFromZero),
    };

    /// <summary>The smallest step a value of this unit is written in.</summary>
    internal double Resolution => Style switch
    {
        PdfNumberStyle.Round or PdfNumberStyle.Truncate => 1,
        PdfNumberStyle.Fraction => 1.0 / Precision,
        _ => Math.Pow(10, -Places),
    };

    private int Places => Math.Clamp((int)Math.Round(Math.Log10(Precision)), 0, 15);

    private string Last(double value)
    {
        switch (Style)
        {
            case PdfNumberStyle.Round or PdfNumberStyle.Truncate:
                return Group(value.ToString("0", CultureInfo.InvariantCulture));
            case PdfNumberStyle.Fraction:
            {
                var whole = Math.Floor(value);
                var numerator = (long)Math.Round((value - whole) * Precision, MidpointRounding.AwayFromZero);
                long denominator = Precision;
                if (numerator == denominator)
                {
                    (whole, numerator) = (whole + 1, 0);
                }

                if (!FixedDenominator && numerator > 0)
                {
                    var divisor = Gcd(numerator, denominator);
                    (numerator, denominator) = (numerator / divisor, denominator / divisor);
                }

                var text = Group(whole.ToString("0", CultureInfo.InvariantCulture));
                return numerator == 0 ? text : $"{text} {numerator}/{denominator}";
            }
            default:
            {
                var text = value.ToString("F" + Places, CultureInfo.InvariantCulture);
                var dot = text.IndexOf('.');
                return dot < 0 ? Group(text) : Group(text[..dot]) + DecimalSeparator + text[(dot + 1)..];
            }
        }
    }

    private string Group(string digits)
    {
        var negative = digits.StartsWith('-');
        var body = negative ? digits[1..] : digits;
        var grouped = new StringBuilder();
        for (var i = 0; i < body.Length; i++)
        {
            if (i > 0 && (body.Length - i) % 3 == 0)
            {
                grouped.Append(ThousandsSeparator);
            }

            grouped.Append(body[i]);
        }

        return (negative ? "-" : "") + grouped;
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return Math.Max(1, a);
    }

    private static string Text(PdfObject value, IPdfObjectResolver resolver) =>
        (resolver.Resolve(value) as PdfString)?.ToText();
}
