using System;
using System.Collections.Generic;
using Adamantium.Fonts;
using Adamantium.Fonts.TextureGeneration;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using static Adamantium.Graphics.Fonts.Layout.TextGeometry;

namespace Adamantium.Graphics.Fonts.Layout;

internal sealed class GlyphQuadBuilder
{
    private const double LineTolerance = 0.01;
    private const float FieldReach = 0.5f;
    private const float MostGrowth = 0.45f;

    private readonly List<FontItem> _reflections = [];
    private readonly List<FontItem> _shadows = [];
    private readonly List<FontItem> _glows = [];
    private readonly List<FontItem> _outlines = [];
    private FontItem[] _items = Array.Empty<FontItem>();
    private int _atlasVersion = -1;

    public RectangleF? Window { get; private set; }

    public uint ElementsCount { get; private set; }

    public FontAtlas FontAtlas { get; private set; }

    public bool NeedsGlyphRefresh => FontAtlas != null && (FontAtlas.IsDisposed || FontAtlas.Version != _atlasVersion);

    public FontAtlas EnsureAtlas(IGraphicsDevice graphicsDevice)
    {
        if (FontAtlas == null || FontAtlas.IsDisposed)
        {
            FontAtlas = FontAtlasStore.GetOrCreateFrom(graphicsDevice,
                FontParameters.Default(sortingVariant: GlyphSortingVariant.ByIndex));
        }

        return FontAtlas;
    }

    public void Clear()
    {
        ElementsCount = 0;
    }

    public IEnumerable<(IFont Font, Glyph Glyph)> GetGlyphs(LaidOutText text)
    {
        var data = text?.Glyphs;
        if (data == null)
        {
            yield break;
        }

        foreach (var word in data)
        {
            if (IsBlank(word.Symbol) || word.Symbol == '\n' || ObjectSizeOf(word.Symbol, word.Attributes) != null
                || !InWindow(text, word))
            {
                continue;
            }

            var font = DrawnFont(word.Font ?? text.Font);
            var paint = font.GetColorPaint(word.Glyph.Index);
            if (paint.Count > 0)
            {
                foreach (var operation in paint)
                {
                    if (operation.Kind == ColorPaintOperationKind.PushClip)
                    {
                        yield return (font, font.GetGlyphByIndex(operation.GlyphIndex));
                    }
                }

                continue;
            }

            var layers = font.GetColorLayers(word.Glyph.Index);
            if (layers.Count == 0)
            {
                foreach (var field in FieldsOf(word.Font ?? text.Font, word.Glyph))
                {
                    yield return field;
                }

                continue;
            }

            foreach (var layer in layers)
            {
                yield return (font, font.GetGlyphByIndex(layer.GlyphIndex));
            }
        }

        foreach (var field in FieldsOf(text.Font, text.DotGlyph))
        {
            yield return field;
        }
    }

    public void Build(IGraphicsDevice graphicsDevice, LaidOutText text, RectangleF? window,
        GlyphEffectDefaults effects = default)
    {
        Window = window;
        // Asked for, not waited for: glyphs land over the next frames and bump the atlas version (NeedsGlyphRefresh).
        var atlas = EnsureAtlas(graphicsDevice);
        atlas.RequestAsync(GetGlyphs(text));
        _atlasVersion = atlas.Version;
        ElementsCount = 0;
        _reflections.Clear();
        _shadows.Clear();
        _glows.Clear();
        _outlines.Clear();

        var glyphs = text.Glyphs;
        var lastPosition = -1;
        for (int i = 0; i < glyphs.Count; ++i)
        {
            var word = glyphs[i];
            if (word.PositionInString >= 0)
            {
                lastPosition = word.PositionInString;
            }

            if (IsBlank(word.Symbol) || word.Symbol == '\n' || ObjectSizeOf(word.Symbol, word.Attributes) != null
                || !InWindow(text, word))
            {
                continue;
            }

            var first = ElementsCount;
            AddWordItems(word);
            if (word.Sideways)
            {
                TurnItems(first, DrawnRect(word));
            }

            var cell = new Vector4F(word.Rect.X, word.Rect.Y, word.Rect.Width, word.Rect.Height);
            for (var k = first; k < ElementsCount; k++)
            {
                _items[k].Cell = cell;
                _items[k].Place.X = word.PositionInString >= 0 ? word.PositionInString : word.Annotates ? -1 : lastPosition;
            }

            AddEffectItems(text, word, first, effects);
        }

        var under = _reflections.Count + _shadows.Count + _glows.Count + _outlines.Count;
        if (under == 0)
        {
            return;
        }

        EnsureItemCapacity((int)ElementsCount + under);
        Array.Copy(_items, 0, _items, under, ElementsCount);
        var at = 0;
        foreach (var layer in (ReadOnlySpan<List<FontItem>>)[_reflections, _shadows, _glows, _outlines])
        {
            layer.CopyTo(_items, at);
            at += layer.Count;
        }

        ElementsCount += (uint)under;
    }

    private void AddEffectItems(LaidOutText text, GlyphWordData word, uint first, GlyphEffectDefaults defaults)
    {
        var attributes = word.Attributes;
        var outline = attributes?.Outline ?? defaults.Outline;
        var shadow = attributes?.Shadow ?? defaults.Shadow;
        var glow = attributes?.Glow ?? defaults.Glow;
        var reflection = attributes?.Reflection ?? defaults.Reflection;
        if (outline == null && shadow == null && glow == null && reflection == null || word.FontSize <= 0)
        {
            return;
        }

        var perPixel = (float)(FontAtlas.MSDFTextureSize / (FontAtlas.PixelRange * word.FontSize));
        var horizontal = (text.Options?.WritingMode ?? WritingMode.Horizontal) == WritingMode.Horizontal;
        var line = word.PositionInString >= 0 && word.PositionInString < text.DropCapEnd
            ? Math.Max(word.LineIndex, text.DropCapLines - 1)
            : word.LineIndex;
        var lines = Math.Min(text.LineTops?.Length ?? 0, text.LineHeights?.Length ?? 0);
        var lineBottom = Math.Max(word.Rect.Bottom, line >= 0 && lines > 0
            ? text.LineTops[Math.Min(line, lines - 1)] + text.LineHeights[Math.Min(line, lines - 1)]
            : word.Rect.Bottom);
        var last = ElementsCount;
        for (var k = first; k < last; k++)
        {
            var item = _items[k];
            if (item.Paint.X > 0.5f)
            {
                continue;
            }

            var room = Math.Max(0, MostGrowth - item.Synthesis.X);
            if (reflection is { Opacity: > 0, Size: > 0 } && Finite(reflection.Distance) && item.Rotation == 0 && horizontal)
            {
                var axis = (float)(lineBottom + Math.Max(0, reflection.Distance));
                var copy = item;
                var quad = item.ArrangeRect;
                copy.ArrangeRect = new Vector4F(quad.X, 2 * axis - (quad.Y + quad.W), quad.Z, quad.W);
                copy.Source = Flipped(item.Source);
                if (item.SecondSource.Z > 0)
                {
                    copy.SecondSource = Flipped(item.SecondSource);
                }

                copy.Synthesis = new Vector4F(item.Synthesis.X, -item.Synthesis.Y, 1 - item.Synthesis.Z, 0);
                copy.Paint = new Vector4F(0, 0, (float)Math.Clamp(reflection.Size, 0.01, 1),
                    (float)Math.Clamp(reflection.Opacity, 0, 1));
                copy.Place.X = -1;
                _reflections.Add(copy);
            }

            if (shadow != null && Finite(shadow.OffsetX) && Finite(shadow.OffsetY) && Finite(shadow.Blur))
            {
                var copy = Colored(item, shadow.Color);
                copy.ArrangeRect.X += (float)shadow.OffsetX;
                copy.ArrangeRect.Y += (float)shadow.OffsetY;
                copy.Synthesis.W = Soft((float)Math.Max(0, shadow.Blur) * perPixel, item.Synthesis.X);
                _shadows.Add(copy);
            }

            if (glow is { Radius: > 0 } && Finite(glow.Radius))
            {
                var radius = (float)glow.Radius * perPixel;
                var grow = Math.Min(radius / 2, room);
                var copy = Colored(item, glow.Color);
                copy.Synthesis.X += grow;
                copy.Synthesis.W = Soft(radius, copy.Synthesis.X);
                _glows.Add(copy);
            }

            if (outline is { Width: > 0 } && Finite(outline.Width))
            {
                var copy = Colored(item, outline.Color);
                copy.Synthesis.X += Math.Min((float)outline.Width * perPixel, room);
                _outlines.Add(copy);
            }
        }
    }

    private static bool Finite(double value) => double.IsFinite(value);

    private static Vector4F Flipped(Vector4F source) => new(source.X, source.Y + source.W, source.Z, -source.W);

    private static FontItem Colored(FontItem item, Color color)
    {
        item.Color = color.ToVector4();
        item.Place.Y = 0;
        return item;
    }

    private static float Soft(float width, float grow) => Math.Max(0, Math.Min(width, 2 * (FieldReach - grow)));

    public FrozenGlyphRun Snapshot(float fontSize)
    {
        var n = (int)ElementsCount;
        var copy = new FontItem[n];
        Array.Copy(_items, copy, n);
        return new FrozenGlyphRun(copy, n, FontAtlas, fontSize) { AtlasVersion = _atlasVersion };
    }

    internal static RectangleF CellSource(GlyphTextureData cell)
    {
        var full = cell.UVRectFull;
        var halfTexelU = (full.Right - full.Left) / (float)cell.FullGlyphSize.Width / 2;
        var halfTexelV = (full.Bottom - full.Top) / (float)cell.FullGlyphSize.Height / 2;
        return new RectangleF
        {
            Left = full.Left + halfTexelU,
            Top = full.Top + halfTexelV,
            Right = full.Right - halfTexelU,
            Bottom = full.Bottom - halfTexelV,
        };
    }

    private bool InWindow(LaidOutText text, GlyphWordData word)
    {
        if (Window is not { } window)
        {
            return true;
        }

        var drawn = DrawnRect(word);
        if (drawn.Right < window.Left || drawn.Left > window.Right)
        {
            return false;
        }

        if (text.LineTops is { } tops && text.LineHeights is { } heights && word.LineIndex >= 0
            && word.LineIndex < Math.Min(tops.Length, heights.Length)
            && (text.Options?.WritingMode ?? WritingMode.Horizontal) == WritingMode.Horizontal)
        {
            var cap = word.PositionInString >= 0 && word.PositionInString < text.DropCapEnd;
            var last = cap
                ? Math.Clamp(text.DropCapLines - 1, word.LineIndex, Math.Min(tops.Length, heights.Length) - 1)
                : word.LineIndex;
            var bottom = cap ? Math.Max(tops[last] + heights[last], drawn.Bottom) : tops[last] + heights[last];
            return bottom > window.Top + LineTolerance && tops[word.LineIndex] < window.Bottom - LineTolerance;
        }

        return drawn.Bottom >= window.Top && drawn.Top <= window.Bottom;
    }

    private void EnsureItemCapacity(int needed)
    {
        if (needed <= _items.Length) return;

        var size = Math.Max(16, _items.Length);
        while (size < needed) size *= 2;
        Array.Resize(ref _items, size);
    }

    private void AddWordItems(GlyphWordData word)
    {
        var font = DrawnFont(word.Font);
        var palette = word.Attributes?.ColorPalette ?? 0;
        if (palette < 0 || palette >= font.ColorPalettes.Count)
        {
            palette = 0;
        }

        var paint = font.GetColorPaint(word.Glyph.Index, palette);
        if (paint.Count > 0)
        {
            AddProgramItem(word, FontAtlas.GetPaintProgram(font, word.Glyph.Index, palette, paint));
            return;
        }

        var layers = font.GetColorLayers(word.Glyph.Index, palette);
        if (layers.Count == 0)
        {
            if (font.ColorBitmapSizes.Count > 0 &&
                FontAtlas.TryGetImageProgram(font, word.Glyph.Index, out var image))
            {
                AddProgramItem(word, image);
                return;
            }

            if (word.Font.Blend != null)
            {
                AddBlendedGlyphItem(word, GlyphColor(word));
                return;
            }

            AddGlyphItem(word, word.Font, word.Glyph, DrawnRect(word), GlyphColor(word), true);
            return;
        }

        var scale = word.FontSize / font.UnitsPerEm;
        foreach (var layer in layers)
        {
            var glyph = font.GetGlyphByIndex(layer.GlyphIndex);
            var rect = DrawnGlyphRect(word, font, glyph, scale);
            var color = layer.Color is { } own ? own.ToVector4() : GlyphColor(word);
            AddGlyphItem(word, font, glyph, rect, color, layer.Color == null);
        }
    }

    private void TurnItems(uint first, RectangleF drawn)
    {
        for (var i = first; i < ElementsCount; i++)
        {
            var quad = _items[i].ArrangeRect;
            _items[i].ArrangeRect = new Vector4F(drawn.X - (quad.Y - drawn.Y), drawn.Y + (quad.X - drawn.X), quad.Z,
                quad.W);
            _items[i].Rotation = (float)(Math.PI / 2);
        }
    }

    private void AddProgramItem(GlyphWordData word, int program)
    {
        if (program < 0)
        {
            return;
        }

        var scale = (float)(word.FontSize / word.Font.UnitsPerEm);
        EnsureItemCapacity((int)ElementsCount + 1);
        _items[ElementsCount] = new FontItem
        {
            ArrangeRect = new Vector4F((float)Pen(word),
                (float)(word.Upright ? Baseline(word) : Math.Round(Baseline(word))), (float)(scale * word.HorizontalScale),
                scale),
            Depth = 1.0f,
            Color = GlyphColor(word),
            Paint = new Vector4F(program + 1, 0, 0, 0),
            Place = new Vector2F(-1, 1)
        };
        ElementsCount++;
    }

    private void AddBlendedGlyphItem(GlyphWordData word, Vector4F color)
    {
        var blend = word.Font.Blend;
        var scale = word.FontSize / word.Font.UnitsPerEm;
        var first = KeyCell(word, blend.From, scale);
        var second = KeyCell(word, blend.To, scale);
        var amount = blend.Amount;
        if (first == null && second == null)
        {
            return;
        }

        if (first == null || second == null)
        {
            first ??= second;
            second ??= first;
            amount = 0;
        }

        var left = Math.Max(first.Value.Quad.X, second.Value.Quad.X);
        var top = Math.Max(first.Value.Quad.Y, second.Value.Quad.Y);
        var right = Math.Min(first.Value.Quad.X + first.Value.Quad.Z, second.Value.Quad.X + second.Value.Quad.Z);
        var bottom = Math.Min(first.Value.Quad.Y + first.Value.Quad.W, second.Value.Quad.Y + second.Value.Quad.W);
        if (right <= left || bottom <= top)
        {
            return;
        }

        var quad = new Vector4F(left, top, right - left, bottom - top);
        EnsureItemCapacity((int)ElementsCount + 1);
        _items[ElementsCount] = new FontItem
        {
            ArrangeRect = quad,
            Source = SourceOver(first.Value, quad),
            Layer = first.Value.Layer,
            Depth = 1.0f,
            Color = color,
            Synthesis = GlyphSynthesis(word, quad),
            SecondSource = SourceOver(second.Value, quad),
            Second = new Vector2F(second.Value.Layer, amount),
            Place = new Vector2F(-1, 1)
        };
        ElementsCount++;
    }

    private (Vector4F Quad, RectangleF Source, float Layer)? KeyCell(GlyphWordData word, IFont key, double scale)
    {
        var glyph = key.GetGlyphByIndex(word.Glyph.Index);
        var gd = FontAtlas.GetGlyphData(key, glyph);
        if (gd == null || gd.BoundingRect.Width <= 0 || gd.BoundingRect.Height <= 0)
        {
            return null;
        }

        var rect = DrawnGlyphRect(word, key, glyph, scale);
        return (CellQuad(word, gd, rect), CellSource(gd), gd.DepthLayer);
    }

    private static Vector4F SourceOver((Vector4F Quad, RectangleF Source, float Layer) cell, Vector4F quad)
    {
        var width = cell.Source.Right - cell.Source.Left;
        var height = cell.Source.Bottom - cell.Source.Top;
        return new Vector4F(
            cell.Source.Left + (quad.X - cell.Quad.X) / cell.Quad.Z * width,
            cell.Source.Top + (quad.Y - cell.Quad.Y) / cell.Quad.W * height,
            quad.Z / cell.Quad.Z * width,
            quad.W / cell.Quad.W * height);
    }

    private Vector4F CellQuad(GlyphWordData word, GlyphTextureData gd, RectangleF rect)
    {
        var margin = gd.Margin - 0.5;
        // Per-axis margin maps the body exactly onto the pixel-snapped rect; a zero-size side falls back to the
        // uniform margin, or the quad collapses.
        var uniform = margin * word.FontSize / FontAtlas.MSDFTextureSize;
        var mx = rect.Width > 0 ? margin * rect.Width / gd.BoundingRect.Width : uniform;
        var my = rect.Height > 0 ? margin * rect.Height / gd.BoundingRect.Height : uniform;
        return new Vector4F((float)(rect.X - mx), (float)(rect.Y - my),
            (float)(rect.Width + 2 * mx), (float)(rect.Height + 2 * my));
    }

    private void AddGlyphItem(GlyphWordData word, IFont font, Glyph glyph, RectangleF rect, Vector4F color,
        bool textColored)
    {
        // The cell (body plus margin) less half a texel on each side: outline/glow/shadow keep the margin, and the
        // filter never reaches the neighbor cell, whose field would draw a sliver of another glyph.
        var gd = FontAtlas.GetGlyphData(font, glyph);
        FontItem item;
        if (gd != null && gd.BoundingRect.Width > 0 && gd.BoundingRect.Height > 0)
        {
            var source = CellSource(gd);
            var quad = CellQuad(word, gd, rect);
            item = new FontItem
            {
                ArrangeRect = (Vector4F)quad,
                Source = (Vector4F)source,
                Layer = gd.DepthLayer,
                Depth = 1.0f,
                Color = color,
                Synthesis = GlyphSynthesis(word, quad)
            };
        }
        else if (gd != null)
        {
            item = new FontItem
            {
                ArrangeRect = (Vector4F)rect,
                Source = (Vector4F)FontAtlas.GetUVCoordinatesForGlyph(font, glyph),
                Layer = gd.DepthLayer,
                Depth = 1.0f,
                Color = color,
                Synthesis = GlyphSynthesis(word, (Vector4F)rect)
            };
        }
        else
        {
            // Not rasterized yet: no quad, or it draws a piece of a neighbor.
            return;
        }

        item.Place = new Vector2F(-1, textColored ? 1 : 0);
        EnsureItemCapacity((int)ElementsCount + 1);
        _items[ElementsCount] = item;
        ElementsCount++;
    }

    private Vector4F GlyphSynthesis(GlyphWordData word, Vector4F quad)
    {
        var embolden = IsEmboldened(word.Attributes)
            ? (float)(FontSynthesisRules.EmboldenPerSide(word.FontSize) * FontAtlas.MSDFTextureSize / FontAtlas.PixelRange)
            : 0f;
        if (!IsSlanted(word.Attributes) || quad.W <= 0)
        {
            return new Vector4F(embolden, 0, 0, 0);
        }

        return new Vector4F(embolden, (float)FontSynthesisRules.Slant, (float)((Baseline(word) - quad.Y) / quad.W), 0);
    }
}
