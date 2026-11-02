using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Fonts;

/// <summary>A letter of a <see cref="TextPath"/>: its cluster, where its middle stands and the way it is turned, and
/// whether it is drawn.</summary>
public readonly record struct TextPathGlyph(GlyphCluster Cluster, Vector2<double> Point, double Angle, bool IsShown);

/// <summary>
/// A line of laid out text along a path, all of what drawing it takes in one place - for a UI element, a map's label, a
/// control in a 3D scene alike: the letters stood on the path where <see cref="TextPathOptions"/> say (alignment, an
/// offset along and across, kept upright, round a closed path across its seam), what is done with text longer than the
/// path (<see cref="TextPathOverflow"/>), the <see cref="GlyphEffect"/>s that draw it, its bounds and which letter a
/// point is on. The path is in the space the text is drawn in; a curve in 3D is projected first, or laid in its plane.
/// The layout is laid out by the caller, on one line. Keeps what it worked out between calls: not thread safe.
/// </summary>
public sealed class TextPath
{
    private readonly TextLayout _given;
    private readonly List<Vector2<double>> _points = [];
    private readonly List<Vector2<double>> _walk = [];
    private readonly List<GlyphOnPath> _placed = [];
    private readonly List<TextPathGlyph> _glyphs = [];
    private TextLayout _trimmed;
    private TextLayout _clustersOf;
    private int _clustersRevision = -1;
    private GlyphCluster[] _clusters = [];
    private GlyphCluster[] _givenClusters = [];
    private int _givenClustersRevision = -1;
    private (int, int, int, bool, bool) _cutKey;
    private double _acrossY;
    private double _lineHeight;

    /// <summary>A path for the text <paramref name="layout"/> holds, laid out on one line.</summary>
    public TextPath(TextLayout layout)
    {
        _given = layout ?? throw new ArgumentNullException(nameof(layout));
        Layout = layout;
    }

    /// <summary>The layout drawn: the one given, or for <see cref="TextPathOverflow.Ellipsis"/> one of the text cut to
    /// the path.</summary>
    public TextLayout Layout { get; private set; }

    /// <summary>The letters as last placed, one a cluster of <see cref="Layout"/>, in its order.</summary>
    public IReadOnlyList<TextPathGlyph> Glyphs => _glyphs;

    /// <summary>How many of the path's units a unit of the layout takes as last placed: the scale asked for, less for
    /// <see cref="TextPathOverflow.Shrink"/>.</summary>
    public double Scale { get; private set; } = 1;

    /// <summary>Whether a letter of the text was drawn when last placed.</summary>
    public bool IsPlaced { get; private set; }

    /// <summary>Whether the path goes round: closed by its figure, or ending where it starts.</summary>
    public bool IsClosed { get; private set; }

    /// <summary>How long the path is.</summary>
    public double Length { get; private set; }

    /// <summary>Takes the path the text runs along: <paramref name="points"/> in a row, closed when
    /// <paramref name="isClosed"/> or when the last ends where the first starts, to within a thousandth of its
    /// length.</summary>
    public void SetPath(ReadOnlySpan<Vector2<double>> points, bool isClosed)
    {
        _points.Clear();
        foreach (var point in points)
        {
            _points.Add(point);
        }

        Length = MathHelper.Polyline.Length(CollectionsMarshal.AsSpan(_points));
        IsClosed = _points.Count > 2 && Length > 0 &&
                   (isClosed || Vector2<double>.Distance(_points[0], _points[^1]) <= Length * 1e-3);
        if (IsClosed && _points[0] != _points[^1])
        {
            Length += Vector2<double>.Distance(_points[^1], _points[0]);
            _points.Add(_points[0]);
        }
    }

    /// <summary>Stands the text on the path as <paramref name="options"/> say, into <see cref="Glyphs"/>; whether a
    /// letter of it is drawn.</summary>
    public bool Place(TextPathOptions options)
    {
        _glyphs.Clear();
        IsPlaced = false;
        Layout = _given;
        Scale = options.Scale;
        if (_points.Count < 2 || !(Length > 0) || !(options.Scale > 0) || !double.IsFinite(options.StartOffset) ||
            !double.IsFinite(options.AcrossOffset))
        {
            return false;
        }

        var width = _given.CalculatedLayoutSize.Width;
        if (options.Overflow == TextPathOverflow.Shrink && width * options.Scale > Length)
        {
            Scale = Length / width;
        }

        var scaled = width * Scale;
        var read = options.Alignment switch
        {
            TextPathAlignment.Center => Length / 2,
            TextPathAlignment.End => Length - scaled / 2,
            _ => scaled / 2,
        } + options.StartOffset;
        if (options.Overflow == TextPathOverflow.Ellipsis && !IsClosed &&
            (read - scaled / 2 < -1e-9 || read + scaled / 2 > Length + 1e-9))
        {
            if (!CutToPath(read - scaled / 2, read + scaled / 2, out read))
            {
                return false;
            }

            width = Layout.CalculatedLayoutSize.Width;
            scaled = width * Scale;
        }

        var clusters = ClustersOf(Layout);
        if (clusters.Length == 0 || Layout.GetLine(0) is not { Height: > 0 } line)
        {
            return false;
        }

        _lineHeight = line.Height;
        _acrossY = line.Baseline + options.AcrossOffset / Scale;
        var hide = options.Overflow == TextPathOverflow.Hide;
        if (IsClosed && !hide)
        {
            PlaceRound(clusters, width, read, options.Overflow == TextPathOverflow.Extend);
            return IsPlaced;
        }

        if (IsClosed && scaled > Length * (1 + 1e-9))
        {
            return false;
        }

        var reversed = !IsClosed && options.KeepUpright && _points[^1].X < _points[0].X;
        var middle = Walk(read, scaled, reversed, hide);
        var maxTurn = hide ? options.MaxTurn : double.PositiveInfinity;
        if (!TextAlongPath.TryPlace(clusters, width, CollectionsMarshal.AsSpan(_walk), middle, Scale, maxTurn, false,
                _placed))
        {
            return false;
        }

        for (var i = 0; i < clusters.Length; i++)
        {
            var along = read + (clusters[i].X - width / 2) * Scale;
            var shown = hide || options.Overflow == TextPathOverflow.Extend ||
                        along >= -1e-9 && along <= Length + 1e-9;
            _glyphs.Add(new TextPathGlyph(clusters[i], _placed[i].Point, _placed[i].Angle, shown));
            IsPlaced |= shown;
        }

        return IsPlaced;
    }

    private void PlaceRound(GlyphCluster[] clusters, double width, double read, bool extend)
    {
        var loop = CollectionsMarshal.AsSpan(_points);
        foreach (var cluster in clusters)
        {
            var shift = (cluster.X - width / 2) * Scale;
            var along = read + shift;
            along -= Math.Floor(along / Length) * Length;
            var point = MathHelper.Polyline.PointAt(loop, along, out var direction);
            var shown = extend || Math.Abs(shift) <= Length / 2;
            _glyphs.Add(new TextPathGlyph(cluster, point, Math.Atan2(direction.Y, direction.X), shown));
            IsPlaced |= shown;
        }
    }

    /// <summary>The effects that draw <see cref="Layout"/> as last placed, a cluster each; a letter not drawn is
    /// drawn as nothing. A new array each call, for a draw to keep.</summary>
    public GlyphEffect[] Effects()
    {
        var effects = new GlyphEffect[_glyphs.Count];
        for (var i = 0; i < effects.Length; i++)
        {
            var glyph = _glyphs[i];
            effects[i] = glyph.IsShown
                ? TextAlongPath.Effect(glyph.Cluster, new GlyphOnPath(glyph.Point, glyph.Angle), Scale, _acrossY)
                : new GlyphEffect(glyph.Cluster.Start, glyph.Cluster.Length, new Matrix3x3F(0, 0, 0, 0, 0, 0, 0, 0, 1),
                    false, null);
        }

        return effects;
    }

    /// <summary>The box the drawn letters take, as last placed; false when none is drawn.</summary>
    public bool TryGetBounds(out Vector2<double> min, out Vector2<double> max)
    {
        min = new Vector2<double>(double.PositiveInfinity, double.PositiveInfinity);
        max = new Vector2<double>(double.NegativeInfinity, double.NegativeInfinity);
        for (var i = 0; i < _glyphs.Count; i++)
        {
            if (!_glyphs[i].IsShown)
            {
                continue;
            }

            var reach = (Math.Max(Math.Abs(_acrossY), Math.Abs(_lineHeight - _acrossY)) + HalfGap(i)) * Scale;
            var point = _glyphs[i].Point;
            min = new Vector2<double>(Math.Min(min.X, point.X - reach), Math.Min(min.Y, point.Y - reach));
            max = new Vector2<double>(Math.Max(max.X, point.X + reach), Math.Max(max.Y, point.Y + reach));
        }

        return max.X >= min.X;
    }

    /// <summary>Whether <paramref name="point"/> is on a drawn letter, as last placed: in the box of the line's height
    /// it stands in, turned with it, as wide as the room to its neighbors.</summary>
    public bool HitTest(Vector2<double> point)
    {
        for (var i = 0; i < _glyphs.Count; i++)
        {
            var glyph = _glyphs[i];
            if (!glyph.IsShown)
            {
                continue;
            }

            var d = point - glyph.Point;
            var along = (d.X * Math.Cos(glyph.Angle) + d.Y * Math.Sin(glyph.Angle)) / Scale;
            var across = (-d.X * Math.Sin(glyph.Angle) + d.Y * Math.Cos(glyph.Angle)) / Scale;
            if (Math.Abs(along) <= Math.Max(HalfGap(i), _lineHeight / 8) && across >= -_acrossY &&
                across <= _lineHeight - _acrossY)
            {
                return true;
            }
        }

        return false;
    }

    private double Walk(double read, double scaled, bool reversed, bool exact)
    {
        _walk.Clear();
        if (IsClosed)
        {
            for (var loop = 0; loop < 3; loop++)
            {
                for (var i = loop == 0 ? 0 : 1; i < _points.Count; i++)
                {
                    _walk.Add(_points[i]);
                }
            }

            return read - Math.Floor(read / Length) * Length + Length;
        }

        var start = exact ? 0 : Math.Max(0, scaled / 2 - read) + 1;
        var end = exact ? 0 : Math.Max(0, read + scaled / 2 - Length) + 1;
        if (exact)
        {
            AddReading(reversed);
            return read;
        }

        var points = CollectionsMarshal.AsSpan(_points);
        MathHelper.Polyline.PointAt(points, 0, out var first);
        MathHelper.Polyline.PointAt(points, Length, out var last);
        var (head, headWay, tail, tailWay) = reversed
            ? (_points[^1], -last, _points[0], -first)
            : (_points[0], first, _points[^1], last);
        _walk.Add(head - headWay * start);
        AddReading(reversed);
        _walk.Add(tail + tailWay * end);
        return read + start;
    }

    private void AddReading(bool reversed)
    {
        if (!reversed)
        {
            _walk.AddRange(_points);
            return;
        }

        for (var i = _points.Count - 1; i >= 0; i--)
        {
            _walk.Add(_points[i]);
        }
    }

    private double HalfGap(int i)
    {
        var gap = 0.0;
        if (i > 0)
        {
            gap = Math.Max(gap, Math.Abs(_glyphs[i].Cluster.X - _glyphs[i - 1].Cluster.X));
        }

        if (i + 1 < _glyphs.Count)
        {
            gap = Math.Max(gap, Math.Abs(_glyphs[i + 1].Cluster.X - _glyphs[i].Cluster.X));
        }

        return gap / 2;
    }

    private GlyphCluster[] ClustersOf(TextLayout layout)
    {
        if (!ReferenceEquals(_clustersOf, layout) || _clustersRevision != layout.Revision)
        {
            _clusters = layout.GetClusters();
            _clustersOf = layout;
            _clustersRevision = layout.Revision;
        }

        return _clusters;
    }

    private bool CutToPath(double textStart, double textEnd, out double read)
    {
        var from = Math.Max(0, textStart);
        var to = Math.Min(Length, textEnd);
        read = 0;
        if (!(to > from))
        {
            return false;
        }

        var cutStart = textStart < 0;
        var cutEnd = textEnd > Length;
        if (_givenClustersRevision != _given.Revision)
        {
            _givenClusters = _given.GetClusters();
            _givenClustersRevision = _given.Revision;
        }

        var clusters = _givenClusters;
        var first = clusters.Length;
        var last = -1;
        for (var i = 0; i < clusters.Length; i++)
        {
            var along = textStart + clusters[i].X * Scale;
            if (along >= from && along <= to)
            {
                first = Math.Min(first, i);
                last = Math.Max(last, i);
            }
        }

        var dropEnd = cutEnd;
        while (true)
        {
            var laid = Cut(clusters, first, last, cutStart, cutEnd);
            if (laid.CalculatedLayoutSize.Width * Scale <= to - from + 1e-9 || first > last)
            {
                break;
            }

            if (dropEnd)
            {
                last--;
            }
            else
            {
                first++;
            }

            dropEnd = cutStart && cutEnd ? !dropEnd : cutEnd;
        }

        Layout = _trimmed;
        var width = _trimmed.CalculatedLayoutSize.Width * Scale;
        read = cutStart && !cutEnd ? to - width / 2 : cutEnd && !cutStart ? from + width / 2 : (from + to) / 2;
        return true;
    }

    private TextLayout Cut(GlyphCluster[] clusters, int first, int last, bool ellipsisBefore, bool ellipsisAfter)
    {
        var key = (_given.Revision, first, last, ellipsisBefore, ellipsisAfter);
        if (_trimmed != null && _cutKey == key)
        {
            return _trimmed;
        }

        _cutKey = key;
        _trimmed ??= new TextLayout(_given.Typeface, _given.Font);
        _trimmed.CopySettingsFrom(_given);
        var whole = _given.Text ?? string.Empty;
        var start = whole.Length;
        var end = 0;
        for (var i = Math.Max(0, first); i <= last && i < clusters.Length; i++)
        {
            start = Math.Min(start, clusters[i].Start);
            end = Math.Max(end, clusters[i].Start + clusters[i].Length);
        }

        end = Math.Min(end, whole.Length);
        var kept = start < end ? whole[start..end] : string.Empty;
        var before = ellipsisBefore ? "…" : string.Empty;
        var text = before + kept + (ellipsisAfter ? "…" : string.Empty);
        var area = new Size<double>(double.PositiveInfinity, double.PositiveInfinity);
        if (_given.AttributedText is { } attributed)
        {
            var cut = new AttributedText(text, attributed.Defaults);
            foreach (var run in attributed.Runs)
            {
                var runStart = Math.Max(run.Start, start);
                var runEnd = Math.Min(run.End, end);
                if (runEnd > runStart)
                {
                    cut = cut.Apply(runStart - start + before.Length, runEnd - runStart, run.Attributes);
                }
            }

            if (ellipsisBefore && start < whole.Length)
            {
                cut = cut.Apply(0, 1, attributed.AttributesAt(start));
            }

            if (ellipsisAfter && end > 0)
            {
                cut = cut.Apply(text.Length - 1, 1, attributed.AttributesAt(end - 1));
            }

            _trimmed.ProcessText(cut, _given.FontSize, area, TextWrapping.NoWrap, TextTrimming.None,
                HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        }
        else
        {
            _trimmed.ProcessText(text, _given.FontSize, area, TextWrapping.NoWrap, TextTrimming.None,
                HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        }

        return _trimmed;
    }
}
