using System;
using System.Collections.Generic;
using System.Threading;
using Adamantium.Fonts.Text;

namespace Adamantium.Graphics.Fonts;

/// <summary>Text with attributes over ranges of it: features, language, colors, background and lines; paragraphs; ruby
/// annotations; and named paragraph and character styles. What a range ends up with is, each over the one before:
/// the defaults, its paragraph's style, its character style, and what was applied to it itself.</summary>
public sealed class AttributedText
{
    private readonly List<AttributeRun> _own = [];
    private readonly List<ParagraphRun> _paragraphs = [];
    private readonly List<(int Start, int End, CharacterStyle Style)> _characterStyles = [];
    private readonly List<(int Start, int End, ParagraphStyle Style)> _paragraphStyles = [];
    private readonly List<RubyRun> _rubies = [];
    private bool[] _graphemes;
    private readonly object _resolving = new();
    private List<AttributeRun> _runs = [];
    private List<ParagraphRun> _resolvedParagraphs = [];
    // What the resolved lists were made from: the edits of this text, its styles' stamp, and the count of style
    // edits anywhere when that stamp was last found current.
    private int _edits;
    private int _resolvedEdits = -1;
    private long _stamp;
    private long _styleEditsSeen = -1;

    public AttributedText(string text, TextAttributes defaults = null)
    {
        Text = text ?? string.Empty;
        Defaults = defaults ?? TextAttributes.Empty;
        if (Text.Length > 0)
        {
            _own.Add(new AttributeRun(0, Text.Length, TextAttributes.Empty));
        }
    }

    public string Text { get; }

    public TextAttributes Defaults { get; }

    /// <summary>Stretches in text order that cover the text without overlapping, each with its final attributes.</summary>
    public IReadOnlyList<AttributeRun> Runs
    {
        get
        {
            Resolve();
            return _runs;
        }
    }

    /// <summary>Sets <paramref name="attributes"/> over a range: the fields it sets replace those there, the rest stay,
    /// and win over the range's styles.</summary>
    public AttributedText Apply(int start, int length, TextAttributes attributes)
    {
        var end = Math.Min(start + length, Text.Length);
        start = Math.Max(start, 0);
        if (end <= start || attributes == null)
        {
            return this;
        }

        var result = new List<AttributeRun>(_own.Count + 2);
        foreach (var run in _own)
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

        _own.Clear();
        _own.AddRange(result);
        Interlocked.Increment(ref _edits);
        return this;
    }

    /// <summary>Sets a character style over a range, in place of any there; null takes them away.</summary>
    public AttributedText SetCharacterStyle(int start, int length, CharacterStyle style)
    {
        var end = Math.Min(start + length, Text.Length);
        start = Math.Max(start, 0);
        if (end <= start)
        {
            return this;
        }

        Replace(_characterStyles, start, end, style);
        Interlocked.Increment(ref _edits);
        return this;
    }

    /// <summary>The character style set at a UTF-16 index, or null.</summary>
    public CharacterStyle CharacterStyleAt(int index) => StyleAt(_characterStyles, index);

    /// <summary>The paragraphs given their own <see cref="ParagraphAttributes"/> or a paragraph style, in text order, not
    /// overlapping, each with how it finally stands. Text outside them stands as the layout says.</summary>
    public IReadOnlyList<ParagraphRun> Paragraphs
    {
        get
        {
            Resolve();
            return _resolvedParagraphs;
        }
    }

    /// <summary>Makes a range a paragraph of its own, from its first character to the U+2029 that ends it (or the end
    /// of the text); a paragraph starts at the start of the text or after a U+2029. An empty range at the end of the
    /// text is the empty paragraph after a closing U+2029. It replaces any paragraph it overlaps. The fields the
    /// attributes set win over the paragraph's style.</summary>
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
        Interlocked.Increment(ref _edits);
        return this;
    }

    /// <summary>Sets a paragraph style over a paragraph, as <see cref="SetParagraph"/> takes its range, in place of any
    /// it overlaps; null takes them away.</summary>
    public AttributedText SetParagraphStyle(int start, int length, ParagraphStyle style)
    {
        var end = Math.Min(start + length, Text.Length);
        start = Math.Max(start, 0);
        if (end <= start && start != Text.Length)
        {
            return this;
        }

        _paragraphStyles.RemoveAll(run => (run.Start < end && run.End > start) || run.Start == start);
        if (style != null)
        {
            var at = _paragraphStyles.FindIndex(run => run.Start > start);
            _paragraphStyles.Insert(at < 0 ? _paragraphStyles.Count : at, (start, end, style));
        }

        Interlocked.Increment(ref _edits);
        return this;
    }

    /// <summary>The paragraph style set on the paragraph a UTF-16 index is in, or null.</summary>
    public ParagraphStyle ParagraphStyleAt(int index)
    {
        foreach (var (start, end, style) in _paragraphStyles)
        {
            if ((index >= start && index < end) || (index == start && start == end))
            {
                return style;
            }
        }

        return null;
    }

    /// <summary>The ruby bases, in text order, not overlapping.</summary>
    public IReadOnlyList<RubyRun> Rubies => _rubies;

    /// <summary>Sets a ruby annotation over a range, its base, widened to whole characters: the base stays on one line
    /// and the annotation stands centered over it. It replaces any ruby it overlaps; an empty annotation removes them.</summary>
    public AttributedText SetRuby(int start, int length, RubyAnnotation annotation)
    {
        var end = Math.Min(start + length, Text.Length);
        start = Math.Max(start, 0);
        if (end <= start)
        {
            return this;
        }

        _graphemes ??= TextBoundaries.Graphemes(Text);
        start = _graphemes[start] ? start : TextBoundaries.Previous(_graphemes, start);
        end = _graphemes[end] ? end : TextBoundaries.Next(_graphemes, end);

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
        Resolve();
        var paragraphs = _resolvedParagraphs;
        if (index == Text.Length)
        {
            return paragraphs.Count > 0 && paragraphs[^1] is { Length: 0 } last && last.Start == index ? last : null;
        }

        var low = 0;
        var high = paragraphs.Count - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            var run = paragraphs[middle];
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
        foreach (var run in Runs)
        {
            if (index >= run.Start && index < run.End)
            {
                return run.Attributes;
            }
        }

        return Defaults;
    }

    // What the styles are now: the runs are resolved again once one of them changed.
    private long StylesStamp()
    {
        long stamp = 17;
        foreach (var (_, _, style) in _characterStyles)
        {
            stamp = stamp * 31 + style.Stamp();
        }

        foreach (var (_, _, style) in _paragraphStyles)
        {
            stamp = stamp * 31 + style.Stamp();
        }

        return stamp;
    }

    // Built anew and published whole, so a reader on another thread keeps the lists it took.
    private void Resolve()
    {
        var edits = Volatile.Read(ref _edits);
        var styled = _characterStyles.Count + _paragraphStyles.Count > 0;
        var styleEdits = styled ? TextStyleEdits.Count : 0;
        if (Volatile.Read(ref _resolvedEdits) == edits && (!styled || Interlocked.Read(ref _styleEditsSeen) == styleEdits))
        {
            return;
        }

        lock (_resolving)
        {
            var stamp = styled ? StylesStamp() : 0;
            if (_resolvedEdits == edits && stamp == _stamp)
            {
                Interlocked.Exchange(ref _styleEditsSeen, styleEdits);
                return;
            }

            var paragraphs = ResolveParagraphs();
            var runs = ResolveRuns();
            _resolvedParagraphs = paragraphs;
            _runs = runs;
            _stamp = stamp;
            Interlocked.Exchange(ref _styleEditsSeen, styleEdits);
            Volatile.Write(ref _resolvedEdits, edits);
        }
    }

    private List<AttributeRun> ResolveRuns()
    {
        var runs = new List<AttributeRun>(_own.Count + _characterStyles.Count + _paragraphStyles.Count);
        if (_characterStyles.Count == 0 && _paragraphStyles.Count == 0)
        {
            foreach (var run in _own)
            {
                runs.Add(new AttributeRun(run.Start, run.Length, Defaults.With(run.Attributes)));
            }

            return runs;
        }

        // Stretch by stretch, between every edge of an own run, a character style and a paragraph style.
        int own = 0, character = 0, paragraph = 0;
        var start = 0;
        while (start < Text.Length)
        {
            while (_own[own].End <= start)
            {
                own++;
            }

            var end = _own[own].End;
            while (character < _characterStyles.Count && _characterStyles[character].End <= start)
            {
                character++;
            }

            CharacterStyle characterStyle = null;
            if (character < _characterStyles.Count)
            {
                var (styleStart, styleEnd, style) = _characterStyles[character];
                end = Math.Min(end, styleStart > start ? styleStart : styleEnd);
                characterStyle = styleStart <= start ? style : null;
            }

            while (paragraph < _paragraphStyles.Count && _paragraphStyles[paragraph].End <= start)
            {
                paragraph++;
            }

            ParagraphStyle paragraphStyle = null;
            if (paragraph < _paragraphStyles.Count)
            {
                var (styleStart, styleEnd, style) = _paragraphStyles[paragraph];
                end = Math.Min(end, styleStart > start ? styleStart : styleEnd);
                paragraphStyle = styleStart <= start ? style : null;
            }

            var attributes = Defaults;
            if (paragraphStyle != null)
            {
                attributes = attributes.With(paragraphStyle.ResolvedText);
            }

            if (characterStyle != null)
            {
                attributes = attributes.With(characterStyle.Resolved);
            }

            runs.Add(new AttributeRun(start, end - start, attributes.With(_own[own].Attributes)));
            start = end;
        }

        return runs;
    }

    // Every paragraph with its own attributes or a style, its own fields over the style's.
    private List<ParagraphRun> ResolveParagraphs()
    {
        var resolved = new List<ParagraphRun>(_paragraphs.Count + _paragraphStyles.Count);
        if (_paragraphStyles.Count == 0)
        {
            resolved.AddRange(_paragraphs);
            return resolved;
        }

        // Stretch by stretch between the edges of both, so where they disagree each part takes what covers it.
        var edges = new List<int>((_paragraphs.Count + _paragraphStyles.Count) * 2);
        foreach (var run in _paragraphs)
        {
            edges.Add(run.Start);
            edges.Add(run.End);
        }

        foreach (var (start, end, _) in _paragraphStyles)
        {
            edges.Add(start);
            edges.Add(end);
        }

        edges.Sort();
        int own = 0, styled = 0;
        for (var e = 0; e + 1 < edges.Count; e++)
        {
            var (from, to) = (edges[e], edges[e + 1]);
            if (to <= from)
            {
                continue;
            }

            while (own < _paragraphs.Count && _paragraphs[own].End <= from)
            {
                own++;
            }

            while (styled < _paragraphStyles.Count && _paragraphStyles[styled].End <= from)
            {
                styled++;
            }

            var ownRun = own < _paragraphs.Count && _paragraphs[own].Start <= from ? _paragraphs[own].Attributes : null;
            var style = styled < _paragraphStyles.Count && _paragraphStyles[styled].Start <= from
                ? _paragraphStyles[styled].Style
                : null;
            if (ownRun != null || style != null)
            {
                resolved.Add(new ParagraphRun(from, to - from,
                    style == null ? ownRun : style.ResolvedParagraph.With(ownRun)));
            }
        }

        // The empty paragraph after a closing U+2029.
        var ownLast = _paragraphs.Count > 0 && _paragraphs[^1] is { Length: 0 } emptyOwn && emptyOwn.Start == Text.Length
            ? emptyOwn.Attributes
            : null;
        var styleLast = _paragraphStyles[^1] is var (lastStart, lastEnd, lastStyle) && lastStart == lastEnd
                                                                                    && lastStart == Text.Length
            ? lastStyle
            : null;
        if (ownLast != null || styleLast != null)
        {
            resolved.Add(new ParagraphRun(Text.Length, 0,
                styleLast == null ? ownLast : styleLast.ResolvedParagraph.With(ownLast)));
        }

        return resolved;
    }

    private static void Replace<T>(List<(int Start, int End, T Style)> runs, int start, int end, T style)
        where T : class
    {
        var result = new List<(int Start, int End, T Style)>(runs.Count + 2);
        foreach (var run in runs)
        {
            if (run.End <= start || run.Start >= end)
            {
                result.Add(run);
                continue;
            }

            if (run.Start < start)
            {
                result.Add((run.Start, start, run.Style));
            }

            if (run.End > end)
            {
                result.Add((end, run.End, run.Style));
            }
        }

        if (style != null)
        {
            result.Add((start, end, style));
        }

        result.Sort((left, right) => left.Start.CompareTo(right.Start));
        runs.Clear();
        runs.AddRange(result);
    }

    private static T StyleAt<T>(List<(int Start, int End, T Style)> runs, int index) where T : class
    {
        foreach (var (start, end, style) in runs)
        {
            if (index >= start && index < end)
            {
                return style;
            }
        }

        return null;
    }
}
