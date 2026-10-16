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
    private const float MinPerspectiveW = 0.05f;
    private const float MaxReach = 4096;

    private FontItem[] _items = Array.Empty<FontItem>();
    private int[] _positions = [];
    private RectangleF[] _cells = [];
    private bool[] _textColored = [];
    private int _atlasVersion = -1;
    private bool _vertexBufferDirty;

    public RectangleF? Window { get; private set; }

    public uint ElementsCount { get; private set; }

    public FontAtlas FontAtlas { get; private set; }

    public Buffer<FontItem> VertexBuffer { get; private set; }

    public bool NeedsGlyphRefresh => FontAtlas != null && (FontAtlas.IsDisposed || FontAtlas.Version != _atlasVersion);

    public int EffectPadding(float fontSize) => FontAtlas == null
        ? 0
        : (int)Math.Ceiling(FontAtlas.GlyphMargin * fontSize / FontAtlas.MSDFTextureSize);

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
        _vertexBufferDirty = true;
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
                || !InWindow(word))
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

    public void Build(IGraphicsDevice graphicsDevice, LaidOutText text, RectangleF? window)
    {
        Window = window;
        // Asked for, not waited for: glyphs land over the next frames and bump the atlas version (NeedsGlyphRefresh).
        var atlas = EnsureAtlas(graphicsDevice);
        atlas.RequestAsync(GetGlyphs(text));
        _atlasVersion = atlas.Version;
        ElementsCount = 0;
        // No vertex buffer here: only the direct draw path needs one (EnsureVertexBuffer), and one per block ran the BAR
        // heap out of memory.

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
                || !InWindow(word))
            {
                continue;
            }

            var first = ElementsCount;
            AddWordItems(word);
            if (word.Sideways)
            {
                TurnItems(first, DrawnRect(word));
            }

            for (var k = first; k < ElementsCount; k++)
            {
                _positions[k] = word.PositionInString >= 0 ? word.PositionInString : lastPosition;
                _cells[k] = word.Rect;
            }
        }

        _vertexBufferDirty = true;
    }

    public void EnsureVertexBuffer(IGraphicsDevice graphicsDevice)
    {
        if (VertexBuffer == null || VertexBuffer.ElementCount < ElementsCount)
        {
            if (VertexBuffer != null)
            {
                graphicsDevice.AddToDeferDisposeQueue(VertexBuffer);
            }

            VertexBuffer = Adamantium.Graphics.Buffer.Vertex.New<FontItem>(graphicsDevice,
                (uint)Math.Max(_items.Length, 1), Adamantium.Graphics.BufferMemoryUsage.UploadFromCpuToGpu);
            _vertexBufferDirty = true;
        }

        if (_vertexBufferDirty)
        {
            VertexBuffer.SetData((ReadOnlySpan<FontItem>)_items.AsSpan(0, (int)ElementsCount));
            _vertexBufferDirty = false;
        }
    }

    public bool TryBakeWorldGlyphs(FontItem[] dest, ref int count, Matrix4x4F world, Vector2F textAreaOffset, Vector4F color)
    {
        if (ElementsCount == 0) return true;

        const float eps = 1e-4f;
        if (Math.Abs(world.M12) > eps || Math.Abs(world.M21) > eps) return false;
        if (count + (int)ElementsCount > dest.Length) return false;

        var sx = world.M11;
        var sy = world.M22;
        var tx = world.M41;
        var ty = world.M42;
        for (var i = 0; i < ElementsCount && Math.Abs(sx - sy) > eps; i++)
        {
            if (_items[i].Rotation != 0)
            {
                return false;
            }
        }

        for (int i = 0; i < ElementsCount; i++)
        {
            var item = _items[i];
            var d = item.ArrangeRect;
            item.ArrangeRect = new Vector4F(
                (d.X + textAreaOffset.X) * sx + tx,
                (d.Y + textAreaOffset.Y) * sy + ty,
                d.Z * sx,
                d.W * sy);
            if (!item.HasOwnColor)
            {
                item.Color = color;
            }
            dest[count++] = item;
        }
        return true;
    }

    public FrozenGlyphRun Snapshot(float fontSize, IReadOnlyList<GlyphEffect> effects)
    {
        var n = (int)ElementsCount;
        var copy = new FontItem[n];
        Array.Copy(_items, copy, n);
        var reach = 0f;
        if (effects is { Count: > 0 })
        {
            for (var i = 0; i < n; i++)
            {
                reach = Math.Max(reach, ApplyEffects(ref copy[i], i, effects));
            }
        }

        return new FrozenGlyphRun(copy, n, FontAtlas, fontSize, reach);
    }

    private float ApplyEffects(ref FontItem item, int index, IReadOnlyList<GlyphEffect> effects)
    {
        var position = _positions[index];
        if (position < 0)
        {
            return 0;
        }

        var cell = _cells[index];
        var total = Matrix3x3F.Identity;
        var touched = false;
        for (var e = 0; e < effects.Count; e++)
        {
            var effect = effects[e];
            if (position < effect.Start || position >= effect.End)
            {
                continue;
            }

            touched = true;
            var transform = effect.Transform;
            if (effect.AroundGlyph)
            {
                var pivotX = cell.X + cell.Width / 2;
                var pivotY = cell.Y + cell.Height / 2;
                transform = Move(-pivotX, -pivotY) * transform * Move(pivotX, pivotY);
            }

            if (effect.Shift != default)
            {
                transform *= Move(effect.Shift.X * cell.Width, effect.Shift.Y * cell.Height);
            }

            total *= transform;
            if (effect.Color is { } color && _textColored[index])
            {
                item.Color = color;
            }
        }

        if (!touched || total.IsIdentity)
        {
            return 0;
        }

        item.Effect = new Vector4F(total.M11 - 1, total.M12, total.M21, total.M22 - 1);
        item.EffectOffset = new Vector2F(total.M31, total.M32);
        item.EffectPerspective = new Vector4F(total.M13, total.M23, total.M33 - 1, 0);
        if (item.Paint.X == 0)
        {
            var quad = item.ArrangeRect;
            cell = RectangleF.Union(cell, new RectangleF(quad.X, quad.Y, quad.Z, quad.W));
        }

        var reach = 0f;
        Span<Vector2F> corners =
        [
            new(cell.Left, cell.Top), new(cell.Right, cell.Top), new(cell.Left, cell.Bottom), new(cell.Right, cell.Bottom)
        ];
        foreach (var corner in corners)
        {
            var w = corner.X * total.M13 + corner.Y * total.M23 + total.M33;
            if (w < MinPerspectiveW)
            {
                return MaxReach;
            }

            var x = (corner.X * total.M11 + corner.Y * total.M21 + total.M31) / w;
            var y = (corner.X * total.M12 + corner.Y * total.M22 + total.M32) / w;
            reach = Math.Max(reach, Math.Max(Math.Abs(x - corner.X), Math.Abs(y - corner.Y)));
        }

        return Math.Min(reach, MaxReach);
    }

    private static Matrix3x3F Move(float x, float y) => new(1, 0, 0, 0, 1, 0, x, y, 1);

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

    private bool InWindow(GlyphWordData word)
    {
        if (Window is not { } window)
        {
            return true;
        }

        var drawn = DrawnRect(word);
        return drawn.Right >= window.Left && drawn.Left <= window.Right
               && drawn.Bottom >= window.Top && drawn.Top <= window.Bottom;
    }

    private void EnsureItemCapacity(int needed)
    {
        if (needed <= _items.Length) return;

        var size = Math.Max(16, _items.Length);
        while (size < needed) size *= 2;
        Array.Resize(ref _items, size);
        Array.Resize(ref _positions, size);
        Array.Resize(ref _cells, size);
        Array.Resize(ref _textColored, size);
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
            Paint = new Vector4F(program + 1, 0, 0, 0)
        };
        _textColored[ElementsCount] = true;
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
            Second = new Vector2F(second.Value.Layer, amount)
        };
        _textColored[ElementsCount] = true;
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
                ArrangeRect = quad,
                Source = source,
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
                ArrangeRect = rect,
                Source = FontAtlas.GetUVCoordinatesForGlyph(font, glyph),
                Layer = gd.DepthLayer,
                Depth = 1.0f,
                Color = color,
                Synthesis = GlyphSynthesis(word, rect)
            };
        }
        else
        {
            // Not rasterized yet: no quad, or it draws a piece of a neighbor.
            return;
        }

        EnsureItemCapacity((int)ElementsCount + 1);
        _items[ElementsCount] = item;
        _textColored[ElementsCount] = textColored;
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
