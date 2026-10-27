using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Adamantium.Core.Fonts.Shaping;

/// <summary>An OpenType feature set to a value over a range of text, in UTF-16 offsets with an exclusive end.</summary>
public readonly partial struct FontFeature : IEquatable<FontFeature>
{
    public const int GlobalStart = 0;
    public const int GlobalEnd = int.MaxValue;

    public FontFeature(string tag, uint value = 1, int start = GlobalStart, int end = GlobalEnd)
    {
        if (string.IsNullOrEmpty(tag) || tag.Length > 4)
        {
            throw new ArgumentException($"'{tag}' is not an OpenType feature tag.", nameof(tag));
        }

        Tag = tag.PadRight(4);
        Value = value;
        Start = start;
        End = end;
    }

    /// <summary>The four-character feature tag, such as <c>liga</c> or <c>ss01</c>.</summary>
    public string Tag { get; }

    /// <summary>0 turns the feature off, 1 on; higher values pick an alternate.</summary>
    public uint Value { get; }

    public int Start { get; }

    public int End { get; }

    public bool IsGlobal => Start == GlobalStart && End == GlobalEnd;

    /// <summary>Parses <c>liga</c>, <c>-liga</c>, <c>liga=0</c>, <c>salt=2</c>, <c>kern[3:5]=0</c> or <c>smcp[2]</c>.</summary>
    public static FontFeature Parse(string text)
    {
        if (!TryParse(text, out var feature))
        {
            throw new FormatException($"'{text}' is not a font feature.");
        }

        return feature;
    }

    public static bool TryParse(string text, out FontFeature feature)
    {
        feature = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.Trim();
        uint value = 1;
        var position = 0;
        if (span[0] is '+' or '-')
        {
            value = span[0] == '-' ? 0u : 1u;
            position = 1;
        }

        var tagStart = position;
        while (position < span.Length && span[position] != '[' && span[position] != '=')
        {
            position++;
        }

        var tag = span.Substring(tagStart, position - tagStart).Trim();
        if (tag.Length is 0 or > 4)
        {
            return false;
        }

        var start = GlobalStart;
        var end = GlobalEnd;
        if (position < span.Length && span[position] == '[')
        {
            var close = span.IndexOf(']', position);
            if (close < 0 || !TryParseRange(span.Substring(position + 1, close - position - 1), out start, out end))
            {
                return false;
            }

            position = close + 1;
        }

        if (position < span.Length)
        {
            if (span[position] != '=' || !uint.TryParse(span.Substring(position + 1).Trim(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out value))
            {
                return false;
            }
        }

        feature = new FontFeature(tag, value, start, end);
        return true;
    }

    /// <summary>Parses a comma-separated list, such as <c>liga=0, ss03, cv05=2</c>, checked as
    /// <see cref="TryParseList"/> checks it.</summary>
    public static IReadOnlyList<FontFeature> ParseList(string text)
    {
        if (!TryParseList(text, out var features, out var error))
        {
            throw new FormatException(error);
        }

        return features;
    }

    /// <summary>
    /// Parses a comma-separated list and checks every tag against the OpenType feature registry. A lowercase tag the
    /// registry does not have is a mistake, reported with the registered tag it most likely meant; a private feature
    /// must have an uppercase letter in its tag, as OpenType requires.
    /// </summary>
    public static bool TryParseList(string text, out IReadOnlyList<FontFeature> features, out string error)
    {
        var list = new List<FontFeature>();
        features = list;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        foreach (var part in text.Split(','))
        {
            if (!TryParse(part, out var feature))
            {
                error = $"'{part.Trim()}' is not a font feature: expected a tag with an optional value, such as liga, " +
                        "-kern, ss01 or cv05=2.";
                return false;
            }

            var tag = feature.Tag.TrimEnd();
            if (!IsPrivateTag(tag) && !FeatureInfos.IsRegistered(tag))
            {
                error = $"'{tag}' is not a registered OpenType feature.{Suggestion(tag)} A private feature needs an " +
                        "uppercase letter in its tag.";
                return false;
            }

            list.Add(feature);
        }

        return true;
    }

    public bool Equals(FontFeature other)
    {
        return Tag == other.Tag && Value == other.Value && Start == other.Start && End == other.End;
    }

    public override bool Equals(object obj) => obj is FontFeature other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Tag?.GetHashCode() ?? 0;
            hash = hash * 397 ^ (int)Value;
            hash = hash * 397 ^ Start;
            return hash * 397 ^ End;
        }
    }

    public override string ToString()
    {
        var text = new StringBuilder(Tag.TrimEnd());
        if (!IsGlobal)
        {
            text.Append('[').Append(Start).Append(':');
            if (End != GlobalEnd)
            {
                text.Append(End);
            }

            text.Append(']');
        }

        if (Value != 1)
        {
            text.Append('=').Append(Value);
        }

        return text.ToString();
    }

    private static bool IsPrivateTag(string tag)
    {
        foreach (var c in tag)
        {
            if (c is >= 'A' and <= 'Z')
            {
                return true;
            }
        }

        return false;
    }

    private static string Suggestion(string tag)
    {
        string best = null;
        var bestDistance = int.MaxValue;
        foreach (var candidate in FeatureInfos.RegisteredTags())
        {
            var distance = EditDistance(tag, candidate);
            if (distance < bestDistance || (distance == bestDistance && string.CompareOrdinal(candidate, best) < 0))
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best != null && bestDistance <= 2 ? $" Did you mean '{best}'?" : string.Empty;
    }

    private static int EditDistance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
        }

        return d[a.Length, b.Length];
    }

    private static bool TryParseRange(string text, out int start, out int end)
    {
        start = GlobalStart;
        end = GlobalEnd;
        var colon = text.IndexOf(':');
        if (colon < 0)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out start))
            {
                return false;
            }

            end = start + 1;
            return true;
        }

        var first = text.Substring(0, colon).Trim();
        var last = text.Substring(colon + 1).Trim();
        if (first.Length > 0 && !int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out start))
        {
            return false;
        }

        return last.Length == 0 || int.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out end);
    }
}
