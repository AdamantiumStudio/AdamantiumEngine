using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Parsers;
using Adamantium.Fonts.Svg;
using Adamantium.Fonts.Tables;
using Adamantium.Fonts.Tables.CFF;
using Adamantium.Fonts.Tables.CMAP;
using Adamantium.Mathematics;

namespace Adamantium.Fonts
{
    public class Font : IFont
    {
        private List<Glyph> glyphs;
        private List<UInt32> unicodes;
        private Dictionary<string, Glyph> nameToGlyph;
        private Dictionary<UInt32, Glyph> unicodeToGlyph;
        private Dictionary<string, List<Feature>> featuresMap;
        private ushort[] advanceWidths;
        private short[] leftSideBearings;
        private Dictionary<string, Font> instances = new();
        private Font baseFont;
        private ushort[] advanceHeights;
        private short[] topSideBearings;
        private bool trueTypeOutlines;
        private short defaultVerticalOriginY;
        private Dictionary<uint, short> verticalOrigins;
        private Dictionary<string, uint[]> justificationExtenders;
        private float[] coordinates;
        private int[] variedAdvances;
        private readonly ConcurrentDictionary<ulong, ColorLayer[]> colorLayerCache = new();
        private ConcurrentDictionary<ulong, ColorPaintOperation[]> colorPaintCache = new();
        private readonly ConcurrentDictionary<ulong, ColorBitmap> colorBitmapCache = new();
        private readonly Dictionary<uint, Glyph> outlineGlyphs = new();
        private readonly Dictionary<string, Glyph> outlineGlyphsByKey = new();
        private readonly Dictionary<ushort, string> names = new();
        private readonly Lazy<GlyphSubstitutionMap> substitutionMap;
        private bool opticalSizeSet;
        private readonly List<(string Name, Font Font)> movingInstances = [];
        private IReadOnlyList<FontVariation> movingFrom;
        private IReadOnlyList<FontVariation> movingTo;
        private float movingProgress;
        private short capsHeight;
        private short capsHeightOfH;
        public Typeface Typeface { get; private set; }
        internal VariationStore VariationData { get; set; }

        public Font(Typeface typeface)
        {
            Typeface = typeface;
            glyphs = new List<Glyph>();
            unicodes = new List<uint>();

            nameToGlyph = new Dictionary<string, Glyph>();
            unicodeToGlyph = new Dictionary<uint, Glyph>();

            featuresMap = new Dictionary<string, List<Feature>>();
            substitutionMap = new Lazy<GlyphSubstitutionMap>(() => new GlyphSubstitutionMap(this));

            Copyright = String.Empty;
            FontFamily = String.Empty;
            FontSubfamily = String.Empty;
            UniqueId = String.Empty;
            FullName = String.Empty;
            Version = String.Empty;
            Trademark = String.Empty;
            Manufacturer = String.Empty;
            Designer = String.Empty;
            Description = String.Empty;
            VendorUrl = String.Empty;
            DesignerUrl = String.Empty;
            LicenseDescription = String.Empty;
            LicenseInfoUrl = String.Empty;
            TypographicFamilyName = String.Empty;
            TypographicSubfamilyName = String.Empty;
            WwsFamilyName = String.Empty;
            WwsSubfamilyName = String.Empty;
            LightBackgroundPalette = String.Empty;
            DarkBackgroundPalette = String.Empty;
        }

        public bool IsGlyphNamesProvided { get; internal set; }

        // Name info section ---
        public string Copyright { get; internal set; }
        public string FontFamily { get; internal set; }
        public string FontSubfamily { get; internal set; }
        public string UniqueId { get; internal set; }
        public string FullName { get; internal set; }
        public string Version { get; internal set; }
        public string Trademark { get; internal set; }
        public string Manufacturer { get; internal set; }
        public string Designer { get; internal set; }
        public string Description { get; internal set; }
        public string VendorUrl { get; internal set; }
        public string DesignerUrl { get; internal set; }
        public string LicenseDescription { get; internal set; }
        public string LicenseInfoUrl { get; internal set; }
        public string TypographicFamilyName { get; internal set; }
        public string TypographicSubfamilyName { get; internal set; }
        public string WwsFamilyName { get; internal set; }
        public string WwsSubfamilyName { get; internal set; }
        public string LightBackgroundPalette { get; internal set; }
        public string DarkBackgroundPalette { get; internal set; }

        /// <inheritdoc />
        public string GetName(ushort nameId) => names.TryGetValue(nameId, out var text) ? text : null;

        internal void SetName(ushort nameId, string text) => names[nameId] = text;

        // ------
        public FeatureCatalog FeatureCatalog { get; } = new FeatureCatalog();

        /// <inheritdoc />
        public IReadOnlyList<GlyphAlternate> GetGlyphAlternates(uint glyphIndex) => substitutionMap.Value.Alternates(glyphIndex);

        public IReadOnlyList<uint> GetGlyphAlternates(uint glyphIndex, string feature, int codepoint, string language) =>
            substitutionMap.Value.Alternates(glyphIndex, feature, codepoint, language, coordinates);

        /// <inheritdoc />
        public IReadOnlyList<string> GetGlyphText(uint glyphIndex) => substitutionMap.Value.Text(glyphIndex);

        internal IReadOnlyDictionary<uint, Glyph> UnicodeToGlyph => unicodeToGlyph;
        public uint GlyphCount => baseFont != null ? baseFont.GlyphCount : (uint)glyphs.Count;
        public ushort UnitsPerEm { get; internal set; }
        public Int16 Ascender { get; internal set; }
        public Int16 Descender { get; internal set; }
        /// <summary>The height of the capitals: the font's own figure, or the top of its H where it gives none.</summary>
        public Int16 CapsHeight
        {
            get
            {
                if (capsHeight != 0)
                {
                    return capsHeight;
                }

                if (capsHeightOfH == 0 && unicodeToGlyph != null && unicodeToGlyph.ContainsKey('H'))
                {
                    var bounds = GetGlyphByUnicode('H').BoundingRectangle;
                    capsHeightOfH = (short)(bounds.Y + bounds.Height);
                }

                return capsHeightOfH;
            }
            internal set => capsHeight = value;
        }

        public Int16 LineAscent { get; internal set; }

        public Int16 LineDescent { get; internal set; }

        public short LineGap { get; internal set; }

        public Int16 UnderlinePosition { get; internal set; }

        public Int16 UnderlineThickness { get; internal set; }

        public Int16 StrikeoutPosition { get; internal set; }

        public Int16 StrikeoutSize { get; internal set; }

        public Int16 SuperscriptYOffset { get; internal set; }

        public Int16 SubscriptYOffset { get; internal set; }

        /// <summary>
        /// smallest readable size in pixels
        /// </summary>
        public UInt16 LowestRecPPEM { get; internal set; }

        /// <summary>
        /// space between lines
        /// </summary>
        public Double LineSpacingMultiplier { get; internal set; }

        public DateTime Created { get; internal set; }

        public DateTime Modified { get; internal set; }

        public IReadOnlyCollection<Glyph> Glyphs => baseFont != null
            ? baseFont.glyphs.Select(glyph => GetGlyphByIndex(glyph.Index)).ToList().AsReadOnly()
            : glyphs.AsReadOnly();
        public IReadOnlyCollection<uint> Unicodes => unicodes.AsReadOnly();
        internal KerningSubtable[] KerningData { get; set; }
        internal OpenTypeLayout Layout { get; } = new OpenTypeLayout();

        float[] IFont.NormalizedCoordinates => coordinates;

        OpenTypeLayout IFont.Layout => Layout;

        bool IFont.TryGetGlyphIndex(int codepoint, out uint glyphIndex)
        {
            if (unicodeToGlyph.TryGetValue((uint)codepoint, out var glyph))
            {
                glyphIndex = glyph.Index;
                return true;
            }

            glyphIndex = 0;
            return false;
        }

        bool IFont.TryGetGlyphIndex(int codepoint, int variationSelector, out uint glyphIndex)
        {
            glyphIndex = 0;
            if (VariationSequences == null ||
                !VariationSequences.TryGetVariant((uint)codepoint, (uint)variationSelector, out glyphIndex, out var isDefault))
            {
                return false;
            }

            return !isDefault || ((IFont)this).TryGetGlyphIndex(codepoint, out glyphIndex);
        }

        internal CharacterMapFormat14 VariationSequences { get; set; }

        internal void SetGlyphs(IEnumerable<Glyph> inputGlyphs)
        {
            glyphs.Clear();
            glyphs.AddRange(inputGlyphs);
        }

        internal void SetHorizontalMetrics(ushort[] advances, short[] bearings)
        {
            advanceWidths = advances;
            leftSideBearings = bearings;
        }

        public FontWeight Weight { get; internal set; } = FontWeight.Normal;

        public FontStyle Style { get; internal set; }

        public FontStretch Stretch { get; internal set; } = FontStretch.Normal;

        /// <inheritdoc />
        public IReadOnlyList<FontAxis> Axes { get; internal set; } = [];

        /// <inheritdoc />
        public IReadOnlyList<FontNamedInstance> NamedInstances { get; internal set; } = [];

        /// <inheritdoc />
        public IReadOnlyList<FontAxisValue> AxisValues { get; internal set; } = [];

        /// <inheritdoc />
        public string ElidedFallbackName { get; internal set; }

        /// <inheritdoc />
        public IReadOnlyList<FontVariation> Variations { get; private set; } = [];

        internal AxisVariationTable AxisVariations { get; set; }

        internal GlyphVariationTable GlyphVariations { get; set; }

        internal HorizontalMetricsVariationTable MetricsVariations { get; set; }

        internal MetricsVariationTable FontMetricsVariations { get; set; }

        internal bool LineMetricsFromWindows { get; set; }

        internal ColorLayerTable ColorLayers { get; set; }

        internal ColorPaletteTable ColorPaletteTable { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<ColorPalette> ColorPalettes => ColorPaletteTable?.Palettes ?? [];

        /// <inheritdoc />
        public IReadOnlyList<ColorLayer> GetColorLayers(uint glyphIndex) => GetColorLayers(glyphIndex, 0);

        /// <inheritdoc />
        public IReadOnlyList<ColorLayer> GetColorLayers(uint glyphIndex, int palette)
        {
            if (ColorLayers == null)
            {
                return [];
            }

            var chosen = ChoosePalette(palette);
            return colorLayerCache.GetOrAdd(PaletteKey(glyphIndex, chosen),
                _ => ColorLayers.GetLayers(glyphIndex, ColorPaletteTable, chosen));
        }

        private int ChoosePalette(int palette) =>
            ColorPaletteTable != null && palette > 0 && palette < ColorPaletteTable.PaletteCount ? palette : 0;

        private static ulong PaletteKey(uint glyphIndex, int palette) => (ulong)(uint)palette << 32 | glyphIndex;

        internal ColorBitmapTable ColorBitmaps { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<int> ColorBitmapSizes => ColorBitmaps?.Sizes ?? [];

        /// <inheritdoc />
        public ColorBitmap GetColorBitmap(uint glyphIndex, int pixelsPerEm)
        {
            if (ColorBitmaps == null)
            {
                return null;
            }

            return colorBitmapCache.GetOrAdd((ulong)(uint)pixelsPerEm << 32 | glyphIndex,
                _ => ColorBitmaps.GetBitmap(glyphIndex, pixelsPerEm));
        }

        internal ColorPaintTable ColorPaints { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<ColorPaintOperation> GetColorPaint(uint glyphIndex) => GetColorPaint(glyphIndex, 0);

        /// <inheritdoc />
        public IReadOnlyList<ColorPaintOperation> GetColorPaint(uint glyphIndex, int palette)
        {
            if (ColorPaints == null && SvgDocuments == null)
            {
                return [];
            }

            var chosen = ChoosePalette(palette);
            return colorPaintCache.GetOrAdd(PaletteKey(glyphIndex, chosen), _ => PaintOf(glyphIndex, chosen));
        }

        /// <inheritdoc />
        public bool TryGetColorClipBox(uint glyphIndex, out RectangleF box)
        {
            box = default;
            return ColorPaints != null && ColorPaints.TryGetClipBox(glyphIndex, coordinates, out box);
        }

        internal SvgDocumentTable SvgDocuments { get; set; }

        private ColorPaintOperation[] PaintOf(uint glyphIndex, int palette)
        {
            var operations = ColorPaints?.GetOperations(glyphIndex, ColorPaletteTable, palette, coordinates) ?? [];
            if (operations.Length > 0 || SvgDocuments == null)
            {
                return operations;
            }

            return SvgGlyphConverter.Convert(SvgDocuments.GetDocument(glyphIndex), glyphIndex, ColorPaletteTable, palette,
                AddOutlineGlyph);
        }

        private uint AddOutlineGlyph(string key, List<List<OutlinePoint>> contours)
        {
            lock (outlineGlyphs)
            {
                if (outlineGlyphsByKey.TryGetValue(key, out var known))
                {
                    return known.Index;
                }

                var glyph = new Glyph(Typeface.GlyphCount + (uint)outlineGlyphs.Count, OutlineType.CompactFontFormat);
                foreach (var contour in contours)
                {
                    var outline = new Outline();
                    outline.Points.AddRange(contour);
                    glyph.AddOutline(outline);
                }

                glyph.RecalculateBounds(true);
                outlineGlyphs[glyph.Index] = glyph;
                outlineGlyphsByKey[key] = glyph;
                return glyph.Index;
            }
        }

        private bool TryGetOutlineGlyph(uint index, out Glyph glyph)
        {
            if (index < Typeface.GlyphCount)
            {
                glyph = null;
                return false;
            }

            lock (outlineGlyphs)
            {
                return outlineGlyphs.TryGetValue(index, out glyph);
            }
        }

        /// <inheritdoc />
        public IFont GetInstance(IReadOnlyList<FontVariation> variations) =>
            GetInstance(variations, variations.Any(v => v.Tag == OpticalSizeAxis));

        /// <inheritdoc />
        public IFont AtOpticalSize(float size)
        {
            if (opticalSizeSet || Axes.All(axis => axis.Tag != OpticalSizeAxis))
            {
                return this;
            }

            if (Blend != null)
            {
                return baseFont.GetMovingInstance(WithOpticalSize(movingFrom, size), WithOpticalSize(movingTo, size),
                    movingProgress, false);
            }

            var variations = Variations.Where(v => v.Tag != OpticalSizeAxis)
                .Append(new FontVariation(OpticalSizeAxis, size))
                .ToArray();
            return GetInstance(variations, false);
        }

        private const string OpticalSizeAxis = "opsz";
        private const int KeyStepsPerAxis = 18;
        private const double KeyTolerance = 1e-3;
        private const int MovingInstancesKept = 16;

        private static FontVariation[] WithOpticalSize(IReadOnlyList<FontVariation> variations, float size) =>
            variations.Where(v => v.Tag != OpticalSizeAxis).Append(new FontVariation(OpticalSizeAxis, size)).ToArray();

        /// <inheritdoc />
        public IFont GetInstance(IReadOnlyList<FontVariation> from, IReadOnlyList<FontVariation> to, float progress) =>
            GetMovingInstance(from, to, progress, from.Concat(to).Any(v => v.Tag == OpticalSizeAxis));

        /// <inheritdoc />
        public FontBlend Blend { get; private set; }

        private IFont GetMovingInstance(IReadOnlyList<FontVariation> from, IReadOnlyList<FontVariation> to, float progress,
            bool opticalSize)
        {
            if (baseFont != null)
            {
                return baseFont.GetMovingInstance(from, to, progress, opticalSize);
            }

            if (Axes.Count == 0 || Typeface.OutlineSource is not IVariableGlyphOutlineSource variable)
            {
                return this;
            }

            var start = Normalize(from, out _);
            var end = Normalize(to, out _);
            var keys = KeyProgress(start, end);
            var at = Math.Max(0f, Math.Min(1f, progress));
            if (keys.Count < 2)
            {
                return GetInstance(start, opticalSize);
            }

            var name = string.Join(",", start.Concat(end).Select(v => v.Value.ToString("R", CultureInfo.InvariantCulture))) +
                       "@" + at.ToString("R", CultureInfo.InvariantCulture) + (opticalSize ? "|opsz" : "");
            lock (movingInstances)
            {
                foreach (var kept in movingInstances)
                {
                    if (kept.Name == name)
                    {
                        return kept.Font;
                    }
                }
            }

            var k = 0;
            while (k < keys.Count - 2 && at > keys[k + 1])
            {
                k++;
            }

            var exact = Normalize(Lerp(start, end, at), out var normalized);
            var instance = CreateInstance(variable, normalized.Select(n => n / 16384f).ToArray(), exact);
            if (instance == this)
            {
                return this;
            }

            instance.opticalSizeSet = opticalSize;
            instance.movingFrom = from;
            instance.movingTo = to;
            instance.movingProgress = progress;
            instance.Blend = new FontBlend(GetInstance(Lerp(start, end, keys[k]), opticalSize),
                GetInstance(Lerp(start, end, keys[k + 1]), opticalSize), (at - keys[k]) / (keys[k + 1] - keys[k]));
            lock (movingInstances)
            {
                movingInstances.Add((name, instance));
                if (movingInstances.Count > MovingInstancesKept)
                {
                    movingInstances.RemoveAt(0);
                }
            }

            return instance;
        }

        private List<float> KeyProgress(FontVariation[] start, FontVariation[] end)
        {
            var dominant = -1;
            var widest = 0.0;
            for (var a = 0; a < Axes.Count; a++)
            {
                var range = Axes[a].MaxValue - Axes[a].MinValue;
                var moved = range > 0 ? Math.Abs(end[a].Value - start[a].Value) / range : 0;
                if (moved > widest)
                {
                    widest = moved;
                    dominant = a;
                }
            }

            if (dominant < 0)
            {
                return [0f];
            }

            var axis = Axes[dominant];
            var step = (axis.MaxValue - axis.MinValue) / (double)KeyStepsPerAxis;
            double first = start[dominant].Value;
            double last = end[dominant].Value;
            var low = Math.Min(first, last);
            var high = Math.Max(first, last);
            var keys = new List<float> { 0f, 1f };
            for (var j = (int)Math.Floor((low - axis.MinValue) / step) + 1; axis.MinValue + j * step < high; j++)
            {
                var value = axis.MinValue + j * step;
                if (value - low > KeyTolerance * step && high - value > KeyTolerance * step)
                {
                    keys.Add((float)((value - first) / (last - first)));
                }
            }

            keys.Sort();
            return keys;
        }

        private static FontVariation[] Lerp(FontVariation[] start, FontVariation[] end, float at)
        {
            var values = new FontVariation[start.Length];
            for (var a = 0; a < start.Length; a++)
            {
                values[a] = new FontVariation(start[a].Tag, start[a].Value + (end[a].Value - start[a].Value) * at);
            }

            return values;
        }

        private FontVariation[] Normalize(IReadOnlyList<FontVariation> variations, out int[] normalized)
        {
            normalized = new int[Axes.Count];
            var values = new FontVariation[Axes.Count];
            for (var a = 0; a < Axes.Count; a++)
            {
                var axis = Axes[a];
                var value = axis.DefaultValue;
                foreach (var variation in variations)
                {
                    if (variation.Tag == axis.Tag)
                    {
                        value = variation.Value;
                    }
                }

                value = Math.Max(axis.MinValue, Math.Min(axis.MaxValue, value));
                values[a] = new FontVariation(axis.Tag, value);
                normalized[a] = axis.Normalize(value);
                if (AxisVariations != null)
                {
                    normalized[a] = AxisVariations.Map(a, normalized[a]);
                }
            }

            return values;
        }

        private IFont GetInstance(IReadOnlyList<FontVariation> variations, bool opticalSize)
        {
            if (baseFont != null)
            {
                return baseFont.GetInstance(variations, opticalSize);
            }

            if (Axes.Count == 0 || Typeface.OutlineSource is not IVariableGlyphOutlineSource variable)
            {
                return this;
            }

            var values = Normalize(variations, out var normalized);

            if (normalized.All(n => n == 0) && !opticalSize)
            {
                return this;
            }

            lock (instances)
            {
                var key = string.Join(",", normalized) + (opticalSize ? "|opsz" : "");
                if (!instances.TryGetValue(key, out var instance))
                {
                    instance = CreateInstance(variable, normalized.Select(n => n / 16384f).ToArray(), values);
                    instance.opticalSizeSet = opticalSize;
                    instances[key] = instance;
                }

                return instance;
            }
        }

        private Font CreateInstance(IVariableGlyphOutlineSource variable, float[] normalized, FontVariation[] values)
        {
            var typeface = new Typeface { Parser = Typeface.Parser };
            var source = variable.Vary(this, normalized, typeface);
            if (source == null && MetricsVariations == null && FontMetricsVariations == null && ColorPaints?.Varies != true)
            {
                return this;
            }

            var instance = (Font)MemberwiseClone();
            instance.Typeface = typeface;
            instance.baseFont = this;
            instance.coordinates = normalized;
            instance.colorPaintCache = new ConcurrentDictionary<ulong, ColorPaintOperation[]>();
            instance.capsHeightOfH = 0;
            instance.Variations = values;
            instance.Weight = values.FirstOrDefault(v => v.Tag == "wght") is { Tag: not null } weight
                ? new FontWeight(Math.Max(1, Math.Min(1000, (int)Math.Round(weight.Value))))
                : Weight;
            instance.Stretch = values.FirstOrDefault(v => v.Tag == "wdth") is { Tag: not null, Value: > 0 } width
                ? new FontStretch(width.Value)
                : Stretch;
            instance.Style = values.Any(v => v.Tag == "ital" && v.Value >= 0.5f) ? FontStyle.Italic
                : values.Any(v => v.Tag == "slnt" && v.Value != 0) ? FontStyle.Oblique
                : Style;
            if (NamedInstanceAt(values) is { } named)
            {
                var family = string.IsNullOrEmpty(TypographicFamilyName) ? FontFamily : TypographicFamilyName;
                instance.FontSubfamily = named.Name;
                instance.TypographicSubfamilyName = named.Name;
                instance.FullName = $"{family} {named.Name}";
            }

            var count = (int)Typeface.GlyphCount;
            instance.variedAdvances = new int[count];
            for (var i = 0; i < count; i++)
            {
                instance.variedAdvances[i] = -1;
            }

            instance.instances = new Dictionary<string, Font>();
            instance.glyphs = null;
            if (source == null)
            {
                typeface.OutlinesId = Typeface.OutlinesId;
                typeface.SetGlyphFactory(count, index => Typeface.GetGlyphByIndex(index, out var same) ? same : null);
            }
            else
            {
                typeface.SetGlyphFactory(count, index => VariedGlyph(index, source));
            }
            typeface.AddFont(instance);
            typeface.SetDefaultFont();
            if (FontMetricsVariations != null)
            {
                instance.VaryMetrics(FontMetricsVariations, normalized);
            }

            return instance;
        }

        private FontNamedInstance NamedInstanceAt(FontVariation[] values) =>
            NamedInstances.FirstOrDefault(named => !string.IsNullOrEmpty(named.Name) && IsAt(named, values));

        private bool IsAt(FontNamedInstance named, FontVariation[] values)
        {
            if (named.Variations.Count != values.Length || values.Length != Axes.Count)
            {
                return false;
            }

            for (var i = 0; i < values.Length; i++)
            {
                if (Math.Abs(Axes[i].Normalize(named.Variations[i].Value) - Axes[i].Normalize(values[i].Value)) > 1)
                {
                    return false;
                }
            }

            return true;
        }

        private Glyph VariedGlyph(uint index, IGlyphOutlineSource source)
        {
            Typeface.GetGlyphByIndex(index, out var original);
            var glyph = original.IsEmpty ? Glyph.EmptyGlyph(original.Index) : new Glyph(original.Index, original.OutlineType);
            glyph.Name = original.Name;
            glyph.SID = original.SID;
            glyph.ClassDefinition = original.ClassDefinition;
            glyph.AdvanceWidth = original.AdvanceWidth;
            glyph.LeftSideBearing = original.LeftSideBearing;
            glyph.AdvanceHeight = original.AdvanceHeight;
            glyph.TopSideBearing = original.TopSideBearing;
            glyph.SetUnicodes(original.Unicodes);
            if (!original.IsEmpty)
            {
                glyph.SetOutlineSource(source);
            }

            return glyph;
        }

        private void VaryMetrics(MetricsVariationTable table, float[] normalized)
        {
            short Varied(int value, string tag) =>
                (short)Math.Floor(value + table.GetDelta(tag, normalized) + 0.5f);

            Ascender = Varied(Ascender, "hasc");
            Descender = Varied(Descender, "hdsc");
            if (capsHeight != 0)
            {
                capsHeight = Varied(capsHeight, "cpht");
            }

            UnderlinePosition = Varied(UnderlinePosition, "undo");
            UnderlineThickness = Varied(UnderlineThickness, "unds");
            StrikeoutPosition = Varied(StrikeoutPosition, "stro");
            StrikeoutSize = Varied(StrikeoutSize, "strs");
            SuperscriptYOffset = Varied(SuperscriptYOffset, "spyo");
            SubscriptYOffset = Varied(SubscriptYOffset, "sbyo");
            if (LineMetricsFromWindows)
            {
                LineAscent = Varied(LineAscent, "hcla");
                LineDescent = Varied(LineDescent, "hcld");
            }
            else
            {
                LineAscent = Varied(LineAscent, "hasc");
                LineDescent = Math.Abs(Varied(-LineDescent, "hdsc"));
                LineGap = Varied(LineGap, "hlgp");
            }
        }

        public ushort GetAdvanceWidth(uint glyphIndex)
        {
            if (baseFont != null)
            {
                return GetVariedAdvanceWidth(glyphIndex);
            }

            if (advanceWidths != null && glyphIndex < advanceWidths.Length)
            {
                return advanceWidths[glyphIndex];
            }

            return Typeface.GetGlyphByIndex(glyphIndex, out var glyph) ? glyph.AdvanceWidth : (ushort)0;
        }

        private ushort GetVariedAdvanceWidth(uint glyphIndex)
        {
            if (baseFont.MetricsVariations == null || glyphIndex >= variedAdvances.Length)
            {
                if (!Typeface.GetGlyphByIndex(glyphIndex, out var glyph))
                {
                    return 0;
                }

                _ = glyph.HasOutlines;
                return glyph.AdvanceWidth;
            }

            var advance = Volatile.Read(ref variedAdvances[glyphIndex]);
            if (advance < 0)
            {
                var delta = baseFont.MetricsVariations.GetAdvanceDelta(glyphIndex, coordinates);
                advance = Math.Max(0, baseFont.GetAdvanceWidth(glyphIndex) + (int)Math.Floor(delta + 0.5f));
                Volatile.Write(ref variedAdvances[glyphIndex], advance);
            }

            return (ushort)advance;
        }

        public short GetLeftSideBearing(uint glyphIndex)
        {
            if (TryGetOutlineGlyph(glyphIndex, out var outlineGlyph))
            {
                return (short)outlineGlyph.BoundingRectangle.X;
            }

            if (baseFont != null)
            {
                return Typeface.GetGlyphByIndex(glyphIndex, out var varied) ? (short)varied.BoundingRectangle.X : (short)0;
            }

            if (leftSideBearings != null && glyphIndex < leftSideBearings.Length)
            {
                return leftSideBearings[glyphIndex];
            }

            return Typeface.GetGlyphByIndex(glyphIndex, out var glyph) ? glyph.LeftSideBearing : (short)0;
        }

        public bool HasVerticalMetrics => (baseFont ?? this).advanceHeights != null;

        public ushort GetAdvanceHeight(uint glyphIndex)
        {
            var heights = (baseFont ?? this).advanceHeights;
            return heights != null && glyphIndex < heights.Length
                ? heights[glyphIndex]
                : (ushort)Math.Max(0, LineAscent + LineDescent);
        }

        public short GetVerticalOriginY(uint glyphIndex)
        {
            var source = baseFont ?? this;
            if (source.verticalOrigins != null)
            {
                return source.verticalOrigins.TryGetValue(glyphIndex, out var origin) ? origin : source.defaultVerticalOriginY;
            }

            Glyph glyph = null;
            if (!source.trueTypeOutlines
                || (!TryGetOutlineGlyph(glyphIndex, out glyph) && !Typeface.GetGlyphByIndex(glyphIndex, out glyph)))
            {
                return LineAscent;
            }

            var bounds = glyph.BoundingRectangle;
            var top = bounds.Y + bounds.Height;
            var bearings = source.topSideBearings;
            if (bearings != null)
            {
                return (short)(top + (glyphIndex < bearings.Length ? bearings[glyphIndex] : 0));
            }

            var advance = LineAscent + LineDescent;
            return (short)(top + ((advance - bounds.Height) >> 1));
        }

        public IReadOnlyList<uint> GetJustificationExtenders(string scriptTag)
        {
            var source = baseFont ?? this;
            return source.justificationExtenders != null
                   && source.justificationExtenders.TryGetValue(scriptTag, out var glyphs)
                ? glyphs
                : [];
        }

        internal void SetJustificationExtenders(Dictionary<string, uint[]> extenders) => justificationExtenders = extenders;

        internal void SetVerticalOrigins(short defaultY, Dictionary<uint, short> origins)
        {
            defaultVerticalOriginY = defaultY;
            verticalOrigins = origins;
        }

        internal void SetVerticalMetrics(ushort[] heights, short[] topBearings)
        {
            advanceHeights = heights;
            topSideBearings = topBearings;
        }

        internal void MarkTrueTypeOutlines() => trueTypeOutlines = true;

        void IFont.UpdateGlyphNamesCache()
        {
            if (!IsGlyphNamesProvided || baseFont != null) return;

            foreach (var glyph in glyphs)
            {
                var name = glyph.Name;
                if (nameToGlyph.ContainsKey(glyph.Name))
                {
                    name = GetUniqueName(glyph.Name);
                }

                nameToGlyph[name] = glyph;
            }
        }

        private string GetUniqueName(string originalName)
        {
            int count = 1;

            string uniqueName = originalName;
            while (nameToGlyph.ContainsKey(uniqueName))
            {
                uniqueName = $"{originalName}.{count++}";
            }

            return uniqueName;
        }

        void IFont.SetGlyphUnicodes(Dictionary<uint, List<uint>> glyphMapping)
        {
            unicodes.Clear();
            unicodeToGlyph.Clear();

            foreach (var kvp in glyphMapping)
            {
                unicodes.AddRange(kvp.Value);
                foreach (var unicode in kvp.Value)
                {
                    if (Typeface.GetGlyphByIndex(kvp.Key, out var glyph))
                    {
                        unicodeToGlyph[unicode] = glyph;
                    }
                }
            }
        }
        
        public IReadOnlyList<Glyph> TranslateIntoGlyphs(string input)
        {
            var translatedGlyphs = new List<Glyph>();
            foreach (var character in input)
            {
                var glyph = GetGlyphByCharacter(character);
                if (glyph == null)
                {
                    Typeface.GetGlyphByIndex(0U, out glyph);
                }
                // Once per character: the glyph is shared for the font's lifetime, so appending on every measure grew
                // without bound. Consumers only read the first entry.
                if (!glyph.RelatedCharacters.Contains(character))
                {
                    // Locked only on the rare first sight of a character: text is laid out from the layout thread AND
                    // from parallel arrange, and an unguarded Add on a shared List is a torn list, not a wrong number.
                    lock (glyph.RelatedCharacters)
                    {
                        if (!glyph.RelatedCharacters.Contains(character)) glyph.RelatedCharacters.Add(character);
                    }
                }

                translatedGlyphs.Add(glyph);
            }

            return translatedGlyphs;
        }

        public Glyph GetGlyphByIndex(uint index)
        {
            if (!Typeface.GetGlyphByIndex(index, out var glyph) && !TryGetOutlineGlyph(index, out glyph))
            {
                Typeface.GetGlyphByIndex(0, out glyph);
            }

            return glyph;
        }

        public Glyph GetGlyphByName(string name)
        {
            if (!nameToGlyph.TryGetValue(name, out var glyph))
            {
                return null;
            }

            return baseFont != null ? GetGlyphByIndex(glyph.Index) : glyph;
        }

        public Glyph GetGlyphByUnicode(uint unicode)
        {
            if (!unicodeToGlyph.TryGetValue(unicode, out var glyph))
            {
                return baseFont != null ? GetGlyphByIndex(baseFont.glyphs[0].Index) : glyphs[0];
            }

            return baseFont != null ? GetGlyphByIndex(glyph.Index) : glyph;
        }

        public Glyph GetGlyphByCharacter(char character)
        {
            return GetGlyphByUnicode(character);
        }

        public Int16 GetKerningValue(UInt16 leftGlyphIndex, UInt16 rightGlyphIndex)
        {
            if (KerningData == null)
            {
                return 0;
            }

            Int16 kerningValue = 0;

            UInt32 key = SfntParser.GenerateKerningKey(leftGlyphIndex, rightGlyphIndex);

            foreach (var data in KerningData)
            {
                if (!data.KerningValues.ContainsKey(key)) continue;

                kerningValue = data.KerningValues[key];
                break;
            }

            return kerningValue;
        }
    }
}