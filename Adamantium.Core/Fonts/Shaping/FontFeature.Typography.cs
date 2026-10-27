using System;

namespace Adamantium.Core.Fonts.Shaping;

public readonly partial struct FontFeature
{
    /// <summary>Stylistic set <c>ss01</c>…<c>ss20</c>: a designer's group of alternates that work together.</summary>
    public static FontFeature StylisticSet(int number, bool enabled = true)
    {
        if (number is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, "Stylistic sets are numbered 1 to 20.");
        }

        return new FontFeature($"ss{number:00}", enabled ? 1u : 0u);
    }

    /// <summary>Character variant <c>cv01</c>…<c>cv99</c>: the alternates of one character; <paramref name="value"/>
    /// picks among them, 0 turns them off.</summary>
    public static FontFeature CharacterVariant(int number, uint value = 1)
    {
        if (number is < 1 or > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, "Character variants are numbered 1 to 99.");
        }

        return new FontFeature($"cv{number:00}", value);
    }

    /// <summary>Stylistic alternates (<c>salt</c>); <paramref name="value"/> picks one when there are several.</summary>
    public static FontFeature StylisticAlternates(uint value = 1) => new("salt", value);

    /// <summary>Swash forms (<c>swsh</c>); <paramref name="value"/> picks one when there are several.</summary>
    public static FontFeature Swash(uint value = 1) => new("swsh", value);

    /// <summary>Ligatures.</summary>
    public static class Ligatures
    {
        /// <summary>Standard ligatures off (<c>liga=0</c>); they are on by default.</summary>
        public static readonly FontFeature Off = new("liga", 0);

        /// <summary>Contextual ligatures off (<c>clig=0</c>); they are on by default.</summary>
        public static readonly FontFeature ContextualOff = new("clig", 0);

        /// <summary>Discretionary ligatures (<c>dlig</c>), such as ct and st.</summary>
        public static readonly FontFeature Discretionary = new("dlig");

        /// <summary>Historical ligatures (<c>hlig</c>).</summary>
        public static readonly FontFeature Historical = new("hlig");

        /// <summary>Contextual alternates off (<c>calt=0</c>), such as a coding font's arrows and operators.</summary>
        public static readonly FontFeature ContextualAlternatesOff = new("calt", 0);
    }

    /// <summary>Capital forms.</summary>
    public static class Capitals
    {
        /// <summary>Lowercase letters as small capitals (<c>smcp</c>).</summary>
        public static readonly FontFeature Small = new("smcp");

        /// <summary>Capital letters as small capitals (<c>c2sc</c>); with <see cref="Small"/>, all letters.</summary>
        public static readonly FontFeature SmallFromCapitals = new("c2sc");

        /// <summary>Lowercase letters as petite capitals (<c>pcap</c>).</summary>
        public static readonly FontFeature Petite = new("pcap");

        /// <summary>Capital letters as petite capitals (<c>c2pc</c>).</summary>
        public static readonly FontFeature PetiteFromCapitals = new("c2pc");

        /// <summary>Titling capitals for large sizes (<c>titl</c>).</summary>
        public static readonly FontFeature Titling = new("titl");

        /// <summary>One height for upper and lower case (<c>unic</c>).</summary>
        public static readonly FontFeature Unicase = new("unic");

        /// <summary>Punctuation fitted to capitals (<c>case</c>).</summary>
        public static readonly FontFeature CaseSensitive = new("case");
    }

    /// <summary>Figures.</summary>
    public static class Numerals
    {
        /// <summary>Oldstyle figures, with ascenders and descenders (<c>onum</c>).</summary>
        public static readonly FontFeature Oldstyle = new("onum");

        /// <summary>Lining figures, the height of capitals (<c>lnum</c>).</summary>
        public static readonly FontFeature Lining = new("lnum");

        /// <summary>Tabular figures of one width, for columns (<c>tnum</c>).</summary>
        public static readonly FontFeature Tabular = new("tnum");

        /// <summary>Proportional figures (<c>pnum</c>).</summary>
        public static readonly FontFeature Proportional = new("pnum");

        /// <summary>A zero with a slash (<c>zero</c>).</summary>
        public static readonly FontFeature SlashedZero = new("zero");

        /// <summary>Ordinal forms, such as 1st and 2º (<c>ordn</c>).</summary>
        public static readonly FontFeature Ordinal = new("ordn");

        /// <summary>Diagonal fractions, 1/2 as one (<c>frac</c>).</summary>
        public static readonly FontFeature Fractions = new("frac");

        /// <summary>Stacked fractions (<c>afrc</c>).</summary>
        public static readonly FontFeature StackedFractions = new("afrc");
    }

    /// <summary>Raised and lowered forms.</summary>
    public static class Position
    {
        /// <summary>Superscript (<c>sups</c>).</summary>
        public static readonly FontFeature Superscript = new("sups");

        /// <summary>Subscript (<c>subs</c>).</summary>
        public static readonly FontFeature Subscript = new("subs");

        /// <summary>Scientific inferiors, as in chemical formulas (<c>sinf</c>).</summary>
        public static readonly FontFeature ScientificInferior = new("sinf");

        /// <summary>Numerators (<c>numr</c>).</summary>
        public static readonly FontFeature Numerator = new("numr");

        /// <summary>Denominators (<c>dnom</c>).</summary>
        public static readonly FontFeature Denominator = new("dnom");
    }

    /// <summary>Spacing.</summary>
    public static class Kerning
    {
        /// <summary>Kerning off (<c>kern=0</c>); it is on by default.</summary>
        public static readonly FontFeature Off = new("kern", 0);

        /// <summary>Wider spacing for all-capital text (<c>cpsp</c>).</summary>
        public static readonly FontFeature CapitalSpacing = new("cpsp");
    }
}
