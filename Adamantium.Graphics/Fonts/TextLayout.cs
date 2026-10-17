using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core;
using Adamantium.Fonts;
using Adamantium.Fonts.Shaping;
using Adamantium.Fonts.Text;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts.Layout;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Fonts;

public class TextLayout : DisposableObject
{
    private readonly GlyphRunShaper _shaper;
    private readonly LineComposer _composer;
    private readonly GlyphQuadBuilder _quads = new();
    private LaidOutText _laid;
    private TextNavigator _navigator;
    private TextLayoutOptions _laidOutOptions;
    private TextRenderingParameters _previousRenderingParameters;
    private AttributedText _attributed;
    private SpacingRange _glyphScaling = new(1, 1, 1);
    private int _hyphenLimit;
    private FontFallback _fallback;
    private bool _fallbackSet;
    private bool _textUpdated;

    public Guid Guid { get; }

    public Typeface Typeface { get; }
    public IFont Font { get; }

    public uint ElementsCount => _quads.ElementsCount;

    public Size CalculatedLayoutSize { get; private set; }

    /// <summary>
    /// Emits a zero-width glyph for each newline, so a text editor can place the caret there. Skipped by rendering; off
    /// by default.
    /// </summary>
    public bool EmitNewlineCarets { get; set; }

    /// <summary>
    /// How many spaces wide a tab stop is: a tab moves the pen to the next multiple of that width from the line's
    /// start. 4 by default.
    /// </summary>
    public int TabSize { get; set; } = 4;

    /// <summary>
    /// Where tabs stop, in the layout's units from the start of the line, each with how the text after it lines up
    /// and what fills the gap; past the last of them, and when there are none, a tab stops every
    /// <see cref="TabSize"/> spaces.
    /// </summary>
    public IReadOnlyList<TabStop> TabStops { get; set; }

    /// <summary>
    /// Which way the text's paragraphs run: left to right, right to left, or <see cref="TextDirection.Auto"/> (the
    /// default), each paragraph by its first strong character. Right-to-left text inside runs right to left either way,
    /// by the Unicode Bidirectional Algorithm.
    /// </summary>
    public TextDirection Direction { get; set; } = TextDirection.Auto;

    /// <summary>
    /// Where words wrapped by <see cref="TextWrapping.WrapByWords"/> may break across lines:
    /// <see cref="Fonts.Hyphens.Manual"/> (the default) at soft hyphens only.
    /// </summary>
    public Hyphens Hyphens { get; set; } = Hyphens.Manual;

    /// <summary>
    /// The most lines in a row that may end in a hyphen breaking a word, as InDesign's hyphen limit: past it a word is
    /// not broken but goes down whole, unless it is the only word on its line. Hyphens of the text itself, as in
    /// "state-of-the-art", are not counted. 0, the default, sets no limit.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is below 0.</exception>
    public int HyphenLimit
    {
        get => _hyphenLimit;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _hyphenLimit = value;
        }
    }

    /// <summary>
    /// How words wrapped by <see cref="TextWrapping.WrapByWords"/> are broken into lines: a line at a time
    /// (<see cref="Fonts.LineBreaking.Greedy"/>, the default) or a paragraph at a time.
    /// </summary>
    public LineBreaking LineBreaking { get; set; } = LineBreaking.Greedy;

    /// <summary>
    /// Hangs punctuation and hyphens at the edges of lines partly past the margin, as InDesign's Optical Margin
    /// Alignment does, so the text's edge looks straight: the start of lines aligned left or justified, the end of
    /// lines aligned right or justified. Lines of right-to-left paragraphs and centered lines are left as they are.
    /// False by default.
    /// </summary>
    public bool OpticalMarginAlignment { get; set; }

    /// <summary>
    /// The first characters of the text set large across its first lines, the lines running beside them; null (the
    /// default) for none. Text wrapped by <see cref="TextWrapping.WrapByWords"/> only, starting left to right.
    /// </summary>
    public DropCap DropCap { get; set; }

    /// <summary>
    /// The frames text wrapped by <see cref="TextWrapping.WrapByWords"/> flows through in turn, in the text area's
    /// units: columns side by side, or frames anywhere, as InDesign threads them. A frame holds as many lines of the
    /// layout's font as its height takes, at least one, on rows of that font's line height - a line of larger text
    /// runs into the row below; what does not fit into the last frame is not laid out (<see cref="OversetIndex"/>).
    /// Null (the default) lays the text out in the text area.
    /// </summary>
    public IReadOnlyList<RectangleF> Frames { get; set; }

    /// <summary>
    /// Areas the text of <see cref="Frames"/> flows around, in the same units: a line runs in the widest part of its
    /// frame that none of them covers, and skips a row they leave too little of.
    /// </summary>
    public IReadOnlyList<RectangleF> Exclusions { get; set; }

    /// <summary>The index of the first character <see cref="Frames"/> had no room for; the text's length when all of
    /// it was laid out.</summary>
    public int OversetIndex { get; private set; }

    /// <summary>
    /// Which way lines run: across (the default), or down and stacked from right to left. A vertical line is as long as
    /// the text area is high, and the lines fill its width from the right; <see cref="HorizontalTextAlignment"/> places
    /// text along each line (<see cref="HorizontalTextAlignment.Left"/> at its top) and
    /// <see cref="VerticalTextAlignment"/> places the lines across the area (<see cref="VerticalTextAlignment.Top"/> at
    /// its right). Underlines run right of vertical lines, strikethroughs down their middle. Drop caps, carets and hit
    /// testing are for horizontal text only.
    /// </summary>
    public WritingMode WritingMode { get; set; }

    /// <summary>
    /// How wide the spaces between words are, as shares of the font's space: their width as laid out, and how far a
    /// justified line may squeeze and stretch them (InDesign's 80%, 100% and 133% by default). A justified line that
    /// cannot fill its width within the range takes letter spacing next, then stretches its spaces further.
    /// </summary>
    public SpacingRange WordSpacing { get; set; } = SpacingRange.Words;

    /// <summary>
    /// What is added between letters, as shares of the font's space: the width added as laid out, and how far a
    /// justified line may take it below and above that, once its spaces are at their limits. None by default. Cursive
    /// scripts, such as Arabic, are never spaced.
    /// </summary>
    public SpacingRange LetterSpacing { get; set; } = SpacingRange.Letters;

    /// <summary>
    /// How wide glyphs are drawn, as shares of their own width, InDesign's glyph scaling: the width they are laid out
    /// at, and how far a justified line may narrow and widen them once its spaces and letters are at their limits.
    /// 100% throughout by default; the minimum must be above 0. Upright glyphs of vertical text are not scaled.
    /// </summary>
    public SpacingRange GlyphScaling
    {
        get => _glyphScaling;
        set => _glyphScaling = value.Minimum > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Glyphs scale to a positive share of their width.");
    }

    /// <summary>Whether a justified line that cannot fill its width within its spacing takes the wider glyphs its font
    /// offers for justification ('jalt', as the wide letters of Hebrew), before it scales glyphs. True by default.</summary>
    public bool JustificationAlternates { get; set; } = true;

    /// <summary>Whether a justified line of Arabic and other joined scripts stretches its words with kashidas, once
    /// its spaces are at their maximum: a stretched extender between two joined letters of each word, after a seen
    /// or a sad where the word has one, else nearest its end, up to an em each. The extender is the font's
    /// justification glyph ('JSTF'), else its tatweel (U+0640). True by default.</summary>
    public bool Kashidas { get; set; } = true;

    /// <summary>Where the last line of a justified paragraph stands, as InDesign sets it: at its start (the default; on
    /// the right in a right-to-left paragraph), centered, at its end, or justified too
    /// (<see cref="TextRenderingParameters.JustifyLastLine"/> justifies it as well).</summary>
    public HorizontalTextAlignment LastLineAlignment { get; set; } = HorizontalTextAlignment.Left;

    /// <summary>Where a justified line holding a single word stands, as InDesign's single word justification sets it:
    /// at its start (the default), centered, at its end, or justified, its letters spread across the line.</summary>
    public HorizontalTextAlignment SingleWordJustification { get; set; } = HorizontalTextAlignment.Left;

    /// <summary>Space added after each character, in thousandths of an em, as InDesign's tracking; negative draws the
    /// letters closer. 0 by default; <see cref="TextAttributes.Tracking"/> sets it for a range. Cursive scripts, such
    /// as Arabic, are never tracked.</summary>
    public double Tracking { get; set; }

    /// <summary>Where the space between two letters comes from: the font's pairs (the default), the letters' outlines,
    /// as InDesign's optical kerning, or neither. <see cref="TextAttributes.Kerning"/> sets it for a range.</summary>
    public TextKerning Kerning { get; set; }

    /// <summary>The grid the lines' baselines stand on (<see cref="Fonts.BaselineGrid"/>); null, the default, keeps
    /// them where their heights put them.</summary>
    public BaselineGrid BaselineGrid { get; set; }

    /// <summary>How far apart the baselines of lines are, in the layout's units, as WPF's line height: the extra room
    /// over the font's own line height (or what it lacks) is split above and below the text. NaN (the default) takes
    /// the font's. <see cref="LineStacking"/> says whether larger text still raises its line.</summary>
    public double LineHeight { get; set; } = double.NaN;

    /// <summary>Whether a line holding text of a larger size or a taller object grows to take it (the default; text of
    /// the layout's size in another face keeps the set height), or every line is exactly <see cref="LineHeight"/> high,
    /// larger text running into the lines around it.</summary>
    public LineStackingStrategy LineStacking { get; set; } = LineStackingStrategy.MaxHeight;

    /// <summary>Space added between lines, in the layout's units, below every line but the last; 0 by default. A
    /// negative one draws lines closer, down to a pitch of one unit.</summary>
    public double LineSpacing { get; set; }

    /// <summary>The most lines laid out; 0 (the default) for any number. What does not fit is not laid out
    /// (<see cref="OversetIndex"/>), or, with trimming, the last line ends in an ellipsis. A drop cap taller than the
    /// most lines is not set.</summary>
    public int MaxLines { get; set; }

    /// <summary>
    /// The text's language, a BCP 47 tag such as "ru" or "en-GB", for ranges whose attributes name none: picks the
    /// hyphenation patterns, the font's localized glyph forms and the fallback fonts. Null when unknown.
    /// </summary>
    public string Language { get; set; }

    /// <summary>
    /// Where a character the text's font lacks is drawn from: the operating system's fallback fonts unless set; null
    /// draws the font's own missing-glyph box instead.
    /// </summary>
    public FontFallback Fallback
    {
        get => _fallbackSet ? _fallback : FontFallback.System;
        set
        {
            _fallback = value;
            _fallbackSet = true;
        }
    }

    /// <summary>
    /// True to never wait for a fallback font's file to be parsed: a character whose font is still loading takes the
    /// room of the text font's missing glyph and draws nothing, <see cref="HasPendingFonts"/> turns true, and the file
    /// parses on a worker that raises <see cref="TypefaceStore.Loaded"/>; the text is laid out again then. False (the
    /// default) waits, as a render with no next frame must.
    /// </summary>
    public bool LoadFontsInBackground { get; set; }

    /// <summary>True when the last layout drew some characters without their font, which was still loading
    /// (<see cref="LoadFontsInBackground"/>).</summary>
    public bool HasPendingFonts { get; private set; }

    public TextRenderingParameters RenderingParameters { get; private set; }

    public FontAtlas FontAtlas => _quads.FontAtlas;

    public string Text { get; private set; }

    public float FontSize { get; private set; }

    public Size RealTextDimensions { get; private set; }

    /// <summary>The attributed text last laid out; null when it was a plain string.</summary>
    public AttributedText AttributedText => _attributed;

    /// <summary>What laying out one string allocates, by stage. Cumulative.</summary>
    public static long TranslateBytes;
    public static long FeatureBytes;
    public static long WordLoopBytes;
    public static long TailBytes;
    public static int ProcessCount;

    /// <summary>Grows each time the text is laid out anew, so whoever keeps this layout can tell its glyphs changed when
    /// its text and size did not: a new last-line alignment, or kashidas switched off.</summary>
    public int Revision { get; private set; }

    public TextLayout(Typeface typeface, IFont font)
    {
        Guid = Guid.NewGuid();
        Typeface = typeface;
        Font = font;
        _shaper = new GlyphRunShaper(font);
        _composer = new LineComposer(font, _shaper);
        _navigator = new TextNavigator(new LaidOutText { Font = font, Glyphs = null, DotGlyph = _composer.DotGlyph });
    }

    public GlyphWordData[] GetTextData()
    {
        return (_laid?.Glyphs ?? []).ToArray();
    }

    public Size ProcessText(string text,
        double fontSize,
        Size textArea,
        TextWrapping textWrapping,
        TextTrimming textTrimming,
        HorizontalTextAlignment horizontalTextAlignment,
        VerticalTextAlignment verticalTextAlignment,
        bool justifyLastLine = false)
    {
        var @params = Parameters(textArea, textWrapping, textTrimming, horizontalTextAlignment, verticalTextAlignment,
            justifyLastLine);
        var options = Snapshot();

        if (_attributed == null && Text == text && MathHelper.IsZero(FontSize - fontSize)
            && _previousRenderingParameters == @params && options.LaysOutLike(_laidOutOptions))
        {
            return CalculatedLayoutSize;
        }

        _attributed = null;
        Text = text;
        FontSize = (float)fontSize;
        _previousRenderingParameters = @params;

        _textUpdated = true;
        return Lay(text, fontSize, @params, options);
    }

    /// <summary>Lays out text whose ranges carry their own features, language, colors, background and lines.</summary>
    public Size ProcessText(AttributedText text,
        double fontSize,
        Size textArea,
        TextWrapping textWrapping,
        TextTrimming textTrimming,
        HorizontalTextAlignment horizontalTextAlignment,
        VerticalTextAlignment verticalTextAlignment,
        bool justifyLastLine = false)
    {
        var @params = Parameters(textArea, textWrapping, textTrimming, horizontalTextAlignment, verticalTextAlignment,
            justifyLastLine);

        _attributed = text;
        Text = text.Text;
        FontSize = (float)fontSize;
        _previousRenderingParameters = @params;

        _textUpdated = true;
        return ProcessText(text.Text, fontSize, @params);
    }

    public Size ProcessText(string text, double fontSize, TextRenderingParameters renderingParameters)
    {
        return Lay(text, fontSize, renderingParameters, Snapshot());
    }

    /// <summary>Whether the paragraph holding UTF-16 <paramref name="index"/> of the text last laid out runs right to
    /// left: its lines start on the right, where <see cref="HorizontalTextAlignment.Left"/> puts them.</summary>
    public bool IsRightToLeftParagraph(int index)
    {
        return TextParagraphs.IsRightToLeftParagraph(_laid?.Paragraphs, Text,
            _laidOutOptions?.Direction ?? TextDirection.Auto, index);
    }

    /// <summary>The glyphs the laid-out text draws, each with its font, the trimming ellipsis included: what the atlas
    /// has to hold for it.</summary>
    public IEnumerable<(IFont Font, Glyph Glyph)> GetGlyphs() => _quads.GetGlyphs(_laid);

    /// <summary>Glyphs have landed in the atlas since this block built its quads; the render side rebuilds on this.</summary>
    public bool NeedsGlyphRefresh => _quads.NeedsGlyphRefresh;

    /// <summary>Re-run the quad build against the atlas as it stands now (see <see cref="NeedsGlyphRefresh"/>), for the
    /// same <see cref="QuadWindow"/>.</summary>
    public void RefreshGlyphs(IGraphicsDevice graphicsDevice)
    {
        _textUpdated = true;
        Update(graphicsDevice, QuadWindow);
    }

    /// <summary>The part of the layout, in its own units, the last <see cref="Update(IGraphicsDevice, RectangleF?)"/>
    /// built quads for; null when it built them for all of it.</summary>
    public RectangleF? QuadWindow => _quads.Window;

    /// <summary>How many glyphs the text last laid out takes, drawn or not.</summary>
    public int GlyphCount => _laid?.Glyphs?.Count ?? 0;

    /// <summary>This block's atlas, created on first use and again when its device is gone.</summary>
    public FontAtlas EnsureAtlas(IGraphicsDevice graphicsDevice) => _quads.EnsureAtlas(graphicsDevice);

    /// <summary>Builds the quads of every glyph the text draws.</summary>
    public void Update(IGraphicsDevice graphicsDevice) => Update(graphicsDevice, null);

    /// <summary>Builds the quads of only the glyphs that touch <paramref name="window"/>, in the layout's units, and asks
    /// the atlas for only theirs: a long text scrolled through a viewport costs what the viewport shows. Null builds them
    /// all.</summary>
    public void Update(IGraphicsDevice graphicsDevice, RectangleF? window)
    {
        // Built before its first measure (a popup built ahead of its layout pass): retried next frame.
        var laid = _laid;
        if (!_textUpdated || laid?.Glyphs == null) return;

        _quads.Build(graphicsDevice, laid, window);
        _textUpdated = true;
    }

    /// <summary>A frozen copy of the shaped glyphs (call after <see cref="Update"/>), so the render thread never reads
    /// this layout while it is reshaped.</summary>
    public FrozenGlyphRun SnapshotGlyphs() => _quads.Snapshot(FontSize);

    /// <summary><see cref="Update(IGraphicsDevice, RectangleF?)"/> and <see cref="SnapshotGlyphs"/> as one step, so two
    /// callers building different windows of this layout on different threads never freeze each other's.</summary>
    public FrozenGlyphRun BuildGlyphRun(IGraphicsDevice graphicsDevice, RectangleF? window)
    {
        lock (_quads)
        {
            Update(graphicsDevice, window);
            return _quads.Snapshot(FontSize);
        }
    }

    /// <summary>Where the caret stands before each UTF-16 character of <see cref="Text"/>, and after the last one.
    /// Characters drawn by one glyph, such as a ligature, share its width.</summary>
    public CaretStop[] GetCaretStops() => _navigator.GetCaretStops();

    /// <summary>How many visual lines the text takes.</summary>
    public int LineCount => _navigator.LineCount;

    /// <summary>A visual line: the characters it shows and where it stands.</summary>
    public TextLineMetrics GetLine(int lineIndex) => _navigator.GetLine(lineIndex);

    /// <summary>Rectangles covering a UTF-16 range, one per piece of each visual line it spans, as high as the line:
    /// for selection, search hits, backgrounds.</summary>
    public IReadOnlyList<RectangleF> GetRangeRects(int start, int end) => _navigator.GetRangeRects(start, end);

    /// <summary>Backgrounds and lines of the attributed text last laid out: draw the backgrounds under the glyphs and the
    /// lines over them.</summary>
    public IReadOnlyList<TextAdornment> GetAdornments() => _navigator.GetAdornments();

    /// <summary>The objects of the text last laid out, in text order; one trimmed away or past the last line is left
    /// out. Objects are set in horizontal text only: vertical text has none.</summary>
    public IReadOnlyList<TextInlineObject> GetInlineObjects() => _navigator.GetInlineObjects();

    /// <summary>The next place the caret may stand after <paramref name="index"/>: the end of the grapheme there, so an
    /// emoji or a letter with its marks is one step.</summary>
    public int NextCaretStop(int index) => _navigator.NextCaretStop(index);

    public int PreviousCaretStop(int index) => _navigator.PreviousCaretStop(index);

    /// <summary>The word, run of spaces or punctuation mark at <paramref name="index"/>, as a double click selects it.</summary>
    public (int Start, int End) GetWordAt(int index) => _navigator.GetWordAt(index);

    /// <summary>The start of the next word after <paramref name="index"/>, past any spaces: where Ctrl+Right goes.</summary>
    public int NextWordStop(int index) => _navigator.NextWordStop(index);

    /// <summary>The start of the word before <paramref name="index"/>: where Ctrl+Left goes.</summary>
    public int PreviousWordStop(int index) => _navigator.PreviousWordStop(index);

    /// <summary>The grapheme nearest to a point in layout coordinates, and where a click there puts the caret.</summary>
    public TextHit HitTest(double x, double y) => _navigator.HitTest(x, y);

    /// <summary>Where the caret stands for a position, in layout coordinates, and on which visual line.</summary>
    public (double X, int LineIndex) GetCaretPoint(CaretPosition position) => _navigator.GetCaretPoint(position);

    /// <summary>The caret position one grapheme to the left or the right of <paramref name="from"/> on screen, whichever
    /// way the text there runs; past a line's edge, the nearer edge of the line the reading goes on to.</summary>
    public CaretPosition MoveVisually(CaretPosition from, bool toRight) => _navigator.MoveVisually(from, toRight);

    /// <summary>The caret position on the edge a line starts from on screen: the left for a left-to-right paragraph, the
    /// right for a right-to-left one.</summary>
    public CaretPosition GetLineStart(int lineIndex) => _navigator.GetLineStart(lineIndex);

    /// <summary>The caret position where a line ends: before its newline or at the end of the text, on the edge its
    /// paragraph ends on; after the last character on screen when the line wraps.</summary>
    public CaretPosition GetLineEnd(int lineIndex) => _navigator.GetLineEnd(lineIndex);

    /// <summary>The characters lying on screen between two caret positions: on one line, between the two; across lines,
    /// from the upper one to its line's end, every line between, and from the lower line's start. In order of the text,
    /// one range per unbroken run of characters.</summary>
    public IReadOnlyList<(int Start, int End)> GetVisualRanges(CaretPosition anchor, CaretPosition focus) =>
        _navigator.GetVisualRanges(anchor, focus);

    private static TextRenderingParameters Parameters(Size textArea, TextWrapping textWrapping,
        TextTrimming textTrimming, HorizontalTextAlignment horizontalTextAlignment,
        VerticalTextAlignment verticalTextAlignment, bool justifyLastLine)
    {
        if (Double.IsNaN(textArea.Width))
        {
            textArea.Width = Int32.MaxValue;
        }

        if (Double.IsNaN(textArea.Height))
        {
            textArea.Height = Int32.MaxValue;
        }

        return new TextRenderingParameters
        {
            HorizontalTextAlignment = horizontalTextAlignment,
            VerticalTextAlignment = verticalTextAlignment,
            JustifyLastLine = justifyLastLine,
            TextWrapping = textWrapping,
            TextTrimming = textTrimming,
            TextArea = new Rectangle(Vector2F.Zero, textArea)
        };
    }

    private TextLayoutOptions Snapshot() => new()
    {
        TabSize = TabSize,
        TabStops = TabStops?.ToArray() ?? [],
        Direction = Direction,
        Hyphens = Hyphens,
        HyphenLimit = HyphenLimit,
        LineBreaking = LineBreaking,
        OpticalMarginAlignment = OpticalMarginAlignment,
        DropCap = DropCap,
        Frames = Frames?.ToArray(),
        Exclusions = Exclusions?.ToArray(),
        WritingMode = WritingMode,
        WordSpacing = WordSpacing,
        LetterSpacing = LetterSpacing,
        GlyphScaling = GlyphScaling,
        JustificationAlternates = JustificationAlternates,
        LastLineAlignment = LastLineAlignment,
        SingleWordJustification = SingleWordJustification,
        Kashidas = Kashidas,
        Tracking = Tracking,
        Kerning = Kerning,
        BaselineGrid = BaselineGrid,
        LineHeight = LineHeight,
        LineStacking = LineStacking,
        LineSpacing = LineSpacing,
        MaxLines = MaxLines,
        Language = Language,
        Fallback = Fallback,
        LoadFontsInBackground = LoadFontsInBackground,
        EmitNewlineCarets = EmitNewlineCarets,
    };

    private Size Lay(string text, double fontSize, TextRenderingParameters renderingParameters, TextLayoutOptions options)
    {
        Revision++;
        // Empty text clears the previous glyphs, or a recycled row keeps drawing its old text.
        if (string.IsNullOrEmpty(text))
        {
            OversetIndex = 0;
            Publish(new LaidOutText
            {
                Text = text,
                Attributed = _attributed,
                FontSize = fontSize,
                Font = Font,
                Options = _laidOutOptions,
                RenderingParameters = RenderingParameters,
                Glyphs = _laid?.Glyphs == null ? null : [],
                DotGlyph = _composer.DotGlyph,
                HasPendingFonts = HasPendingFonts,
            });
            _quads.Clear();
            CalculatedLayoutSize = Size.Zero;
            RealTextDimensions = Size.Zero;
            _textUpdated = true;
            return Size.Zero;
        }

        RenderingParameters = renderingParameters;
        var laid = _composer.Lay(text, _attributed, fontSize, renderingParameters, options);
        _laidOutOptions = options;
        OversetIndex = laid.OversetIndex;
        HasPendingFonts = laid.HasPendingFonts;
        RealTextDimensions = laid.RealTextDimensions ?? RealTextDimensions;
        if (laid.NoRoom)
        {
            laid = laid with { Glyphs = _laid?.Glyphs == null ? null : [] };
            _quads.Clear();
            _textUpdated = true;
        }

        // One reference write of the result: a reader sees the previous layout or this one, never a half-aligned mixture.
        Publish(laid);
        CalculatedLayoutSize = laid.Size;
        return CalculatedLayoutSize;
    }

    private void Publish(LaidOutText laid)
    {
        _laid = laid;
        _navigator = new TextNavigator(laid);
    }
}

class BackToFrontComparer : IComparer<FontItem>
{
    public int Compare(FontItem left, FontItem rigth)
    {
        return rigth.Depth.CompareTo(left.Depth);
    }
}

class FrontToBackComparer : IComparer<FontItem>
{
    public int Compare(FontItem left, FontItem rigth)
    {
        return left.Depth.CompareTo(rigth.Depth);
    }
}
