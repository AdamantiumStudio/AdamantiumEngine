using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Adamantium.Fonts.Shaping;

/// <summary>Kerning measured from the glyphs' outlines rather than read from the font, as InDesign's optical kerning: a
/// pair is moved so the white between its two glyphs, as far as the eye takes it into their hollows, is what it is
/// between two of the font's straight letters - "nn" and "HH", or their Cyrillic, Greek, Armenian and Georgian
/// likes.</summary>
public static class OpticalKerning
{
    private const string Straight = "nHнНνΗնՆნ";

    private const int Bands = 24;
    private const byte SampleRate = 5;
    private const int MostPairs = 1 << 16;

    // In ems.
    private const double Depth = 0.15;
    private const double Limit = 0.25;
    private const double Clearance = 0.02;

    private static readonly ConditionalWeakTable<IFont, Font> ByFont = new();

    /// <summary>Whether the text from <paramref name="start"/> to <paramref name="end"/> is kerned optically: the scripts
    /// whose letters stand apart in a row - Latin, Greek, Cyrillic, Armenian, Georgian - and the characters every script
    /// shares. A script whose letters join or hang from one line, and one whose characters stand in a grid, keeps the
    /// font's pairs.</summary>
    public static bool Applies(string text, int start, int end) => Run(text, start, end, out var applies) == end && applies;

    /// <summary>Where the stretch of <paramref name="text"/> from <paramref name="start"/> that is or is not kerned
    /// optically (<paramref name="applies"/>, see <see cref="Applies"/>) ends, before <paramref name="end"/>: the
    /// characters every script shares go with the letters before them.</summary>
    public static int Run(string text, int start, int end, out bool applies)
    {
        bool? kind = null;
        for (var i = start; i < end; i++)
        {
            var next = char.IsHighSurrogate(text[i]) && i + 1 < end && char.IsLowSurrogate(text[i + 1]) ? i + 2 : i + 1;
            var codepoint = next == i + 2 ? char.ConvertToUtf32(text[i], text[i + 1]) : text[i];
            var script = UnicodeData.GetScript(codepoint);
            if (script is not ("Zyyy" or "Zinh" or "Zzzz"))
            {
                var stands = script is "Latn" or "Grek" or "Cyrl" or "Armn" or "Geor";
                if (kind is { } known && known != stands)
                {
                    applies = known;
                    return i;
                }

                kind = stands;
            }

            i = next - 1;
        }

        applies = kind ?? true;
        return end;
    }

    /// <summary>Whether pairs of <paramref name="font"/> can be measured: it has a straight letter to measure them
    /// against.</summary>
    public static bool Measures(IFont font) => ByFont.GetValue(font, static f => new Font(f)).HasReference;

    /// <summary>Kerns <paramref name="shaped"/> - glyphs of <paramref name="font"/> in left-to-right order, shaped with
    /// the font's 'kern' off - optically from <paramref name="start"/> to <paramref name="end"/>: each glyph that
    /// advances moves the next one by the pair's kerning, and the marks between them stay on their base.</summary>
    public static void Apply(IFont font, ShapedGlyph[] shaped, int start, int end)
    {
        var previous = -1;
        for (var k = start; k < end; k++)
        {
            if (shaped[k].XAdvance <= 0)
            {
                continue;
            }

            if (previous >= 0)
            {
                var left = shaped[previous];
                var kern = Between(font, left.GlyphIndex, left.XAdvance, shaped[k].GlyphIndex);
                shaped[previous] = new ShapedGlyph(left.GlyphIndex, left.Cluster, left.XAdvance + kern, left.YAdvance,
                    left.XOffset, left.YOffset);
                for (var mark = previous + 1; mark < k; mark++)
                {
                    var on = shaped[mark];
                    shaped[mark] = new ShapedGlyph(on.GlyphIndex, on.Cluster, on.XAdvance, on.YAdvance,
                        on.XOffset - kern, on.YOffset);
                }
            }

            previous = k;
        }
    }

    /// <summary>How far, in font units, the glyph <paramref name="right"/> moves toward <paramref name="left"/> when it
    /// follows it at <paramref name="leftAdvance"/>: added to the left glyph's advance; negative draws them closer, never
    /// so close that their ink at one height comes within 0.02 em. 0 when either glyph draws nothing.</summary>
    public static int Between(IFont font, uint left, int leftAdvance, uint right)
    {
        var measures = ByFont.GetValue(font, static f => new Font(f));
        return measures.Between(left, leftAdvance, right);
    }

    private sealed class Font
    {
        private readonly object _gate = new();
        private readonly IFont _font;
        private readonly double _top;
        private readonly double _depth;
        private readonly double _limit;
        private readonly double _clearance;
        private readonly Dictionary<uint, Profile> _profiles = new();
        private readonly Dictionary<(uint Left, int Advance, uint Right), int> _pairs = new();
        private double? _reference;
        private bool _referenceKnown;

        public Font(IFont font)
        {
            _font = font;
            _top = font.CapsHeight > 0 ? font.CapsHeight : 0.7 * font.UnitsPerEm;
            _depth = Depth * font.UnitsPerEm;
            _limit = Limit * font.UnitsPerEm;
            _clearance = Clearance * font.UnitsPerEm;
        }

        public bool HasReference
        {
            get
            {
                lock (_gate)
                {
                    return Reference().HasValue;
                }
            }
        }

        public int Between(uint left, int leftAdvance, uint right)
        {
            lock (_gate)
            {
                if (_pairs.TryGetValue((left, leftAdvance, right), out var known))
                {
                    return known;
                }

                var kern = 0;
                if (Reference() is { } reference && Gap(left, leftAdvance, right, out var closest) is { } gap)
                {
                    var tightest = Math.Max(-_limit, -Math.Max(0, closest - _clearance));
                    kern = (int)Math.Round(Math.Max(tightest, Math.Min(_limit, reference - gap)));
                }

                if (_pairs.Count >= MostPairs)
                {
                    _pairs.Clear();
                }

                _pairs[(left, leftAdvance, right)] = kern;
                return kern;
            }
        }

        private double? Reference()
        {
            if (_referenceKnown)
            {
                return _reference;
            }

            var sum = 0.0;
            var count = 0;
            foreach (var character in Straight)
            {
                if (_font.TryGetGlyphIndex(character, out var index)
                    && Gap(index, _font.GetAdvanceWidth(index), index, out _) is { } gap)
                {
                    sum += gap;
                    count++;
                }
            }

            _reference = count > 0 ? sum / count : null;
            _referenceKnown = true;
            return _reference;
        }

        private double? Gap(uint left, int leftAdvance, uint right, out double closest)
        {
            closest = double.MaxValue;
            var first = ProfileOf(left);
            var second = ProfileOf(right);
            if (first == null || second == null)
            {
                return null;
            }

            var bands = Math.Max(first.Top, second.Top);
            var sum = 0.0;
            for (var band = 0; band < bands; band++)
            {
                sum += leftAdvance - first.Right(band) + second.Left(band);
                if (first.Inked(band) && second.Inked(band))
                {
                    closest = Math.Min(closest, leftAdvance - first.InkRight(band) + second.InkLeft(band));
                }
            }

            return sum / bands;
        }

        private Profile ProfileOf(uint glyph)
        {
            if (!_profiles.TryGetValue(glyph, out var profile))
            {
                profile = Profile.Of(_font.GetGlyphByIndex(glyph), _top, _depth);
                _profiles[glyph] = profile;
            }

            return profile;
        }
    }

    private sealed class Profile
    {
        private readonly double[] _left = new double[Bands];
        private readonly double[] _right = new double[Bands];
        private readonly double[] _inkLeft = new double[Bands];
        private readonly double[] _inkRight = new double[Bands];

        public int Top { get; private set; }

        public double Left(int band) => _left[band];

        public double Right(int band) => _right[band];

        public bool Inked(int band) => _inkLeft[band] <= _inkRight[band];

        public double InkLeft(int band) => _inkLeft[band];

        public double InkRight(int band) => _inkRight[band];

        public static Profile Of(Glyph glyph, double top, double depth)
        {
            if (glyph == null || glyph.IsEmpty)
            {
                return null;
            }

            List<Mathematics.LineSegment2D> segments;
            try
            {
                segments = glyph.GetMergedOutlineSegments(SampleRate);
            }
            catch (Exception)
            {
                // An outline that cannot be read, as the atlas's own rasterizing finds it: no ink to measure.
                return null;
            }

            if (segments.Count == 0)
            {
                return null;
            }

            var profile = new Profile();
            var outermostLeft = double.MaxValue;
            var outermostRight = double.MinValue;
            for (var band = 0; band < Bands; band++)
            {
                var left = double.MaxValue;
                var right = double.MinValue;
                for (var line = 0; line < 3; line++)
                {
                    var y = top * (band + (line + 0.5) / 3) / Bands;
                    foreach (var segment in segments)
                    {
                        var (from, to) = (segment.Start, segment.End);
                        if ((from.Y <= y) == (to.Y <= y))
                        {
                            continue;
                        }

                        var x = from.X + (y - from.Y) * (to.X - from.X) / (to.Y - from.Y);
                        left = Math.Min(left, x);
                        right = Math.Max(right, x);
                    }
                }

                profile._left[band] = profile._inkLeft[band] = left;
                profile._right[band] = profile._inkRight[band] = right;
                if (left <= right)
                {
                    profile.Top = band + 1;
                    outermostLeft = Math.Min(outermostLeft, left);
                    outermostRight = Math.Max(outermostRight, right);
                }
            }

            if (profile.Top == 0)
            {
                return null;
            }

            for (var band = 0; band < Bands; band++)
            {
                profile._left[band] = Math.Min(profile._left[band], outermostLeft + depth);
                profile._right[band] = Math.Max(profile._right[band], outermostRight - depth);
            }

            return profile;
        }
    }
}
