using System;
using System.Collections.Generic;

namespace Adamantium.Graphics.Fonts;

/// <summary>Text with attributes over ranges of it: features, language, colors, background and lines; paragraphs and
/// ruby annotations.</summary>
public sealed class AttributedText
{
    private readonly List<AttributeRun> _runs = [];
    private readonly List<ParagraphRun> _paragraphs = [];
    private readonly List<RubyRun> _rubies = [];

    public AttributedText(string text, TextAttributes defaults = null)
    {
        Text = text ?? string.Empty;
        Defaults = defaults ?? TextAttributes.Empty;
        if (Text.Length > 0)
        {
            _runs.Add(new AttributeRun(0, Text.Length, Defaults));
        }
    }

    public string Text { get; }

    public TextAttributes Defaults { get; }

    /// <summary>Stretches in text order that cover the text without overlapping, each with its final attributes.</summary>
    public IReadOnlyList<AttributeRun> Runs => _runs;

    /// <summary>Sets <paramref name="attributes"/> over a range: the fields it sets replace those there, the rest stay.</summary>
    public AttributedText Apply(int start, int length, TextAttributes attributes)
    {
        var end = Math.Min(start + length, Text.Length);
        start = Math.Max(start, 0);
        if (end <= start || attributes == null)
        {
            return this;
        }

        var result = new List<AttributeRun>(_runs.Count + 2);
        foreach (var run in _runs)
        {
            if (run.End <= start || run.Start >= end)
            {
                result.Add(run);
                continue;
            }

            if (run.Start < start)
            {
                result.Add(new AttributeRun(run.Start, start - run.Start, run.Attributes));
            }

            var from = Math.Max(run.Start, start);
            var to = Math.Min(run.End, end);
            result.Add(new AttributeRun(from, to - from, run.Attributes.With(attributes)));

            if (run.End > end)
            {
                result.Add(new AttributeRun(end, run.End - end, run.Attributes));
            }
        }

        _runs.Clear();
        _runs.AddRange(result);
        return this;
    }

    /// <summary>The paragraphs given their own <see cref="ParagraphAttributes"/>, in text order, not overlapping. Text
    /// outside them stands as the layout says.</summary>
    public IReadOnlyList<ParagraphRun> Paragraphs => _paragraphs;

    /// <summary>Makes a range a paragraph of its own, from its first character to the U+2029 that ends it (or the end
    /// of the text); a paragraph starts at the start of the text or after a U+2029. An empty range at the end of the
    /// text is the empty paragraph after a closing U+2029. It replaces any paragraph it overlaps.</summary>
    public AttributedText SetParagraph(int start, int length, ParagraphAttributes attributes)
    {
        var end = Math.Min(start + length, Text.Length);
        start = Math.Max(start, 0);
        if (attributes == null || (end <= start && start != Text.Length))
        {
            return this;
        }

        _paragraphs.RemoveAll(run => (run.Start < end && run.End > start) || run.Start == start);
        var at = _paragraphs.FindIndex(run => run.Start > start);
        _paragraphs.Insert(at < 0 ? _paragraphs.Count : at, new ParagraphRun(start, end - start, attributes));
        return this;
    }

    /// <summary>The ruby bases, in text order, not overlapping.</summary>
    public IReadOnlyList<RubyRun> Rubies => _rubies;

    /// <summary>Sets a ruby annotation over a range, its base: the base stays on one line and the annotation stands
    /// centered over it. It replaces any ruby it overlaps; an empty annotation removes them.</summary>
    public AttributedText SetRuby(int start, int length, RubyAnnotation annotation)
    {
        var end = Math.Min(start + length, Text.Length);
        start = Math.Max(start, 0);
        if (end <= start)
        {
            return this;
        }

        _rubies.RemoveAll(run => run.Start < end && run.End > start);
        if (string.IsNullOrEmpty(annotation?.Text))
        {
            return this;
        }

        var at = _rubies.FindIndex(run => run.Start > start);
        _rubies.Insert(at < 0 ? _rubies.Count : at, new RubyRun(start, end - start, annotation));
        return this;
    }

    /// <summary>The paragraph a UTF-16 index is in, or null when it is in none set.</summary>
    public ParagraphRun? ParagraphAt(int index)
    {
        if (index == Text.Length)
        {
            return _paragraphs.Count > 0 && _paragraphs[^1] is { Length: 0 } last && last.Start == index ? last : null;
        }

        var low = 0;
        var high = _paragraphs.Count - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            var run = _paragraphs[middle];
            if (index < run.Start)
            {
                high = middle - 1;
            }
            else if (index >= run.End)
            {
                low = middle + 1;
            }
            else
            {
                return run;
            }
        }

        return null;
    }

    /// <summary>The attributes at a UTF-16 index.</summary>
    public TextAttributes AttributesAt(int index)
    {
        foreach (var run in _runs)
        {
            if (index >= run.Start && index < run.End)
            {
                return run.Attributes;
            }
        }

        return Defaults;
    }
}
