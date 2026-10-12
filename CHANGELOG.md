# Changelog

Notable changes to the Adamantium Engine packages, in the form of [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
All packages share one version.

## Unreleased

### Added

- `IGraphicsDevice.TargetOrigin`: where the target being drawn into lies in the coordinates viewports and scissors are
  given in, for a target that holds one region of the surface. Callers keep setting viewports and scissors in the
  surface's coordinates; the device moves what it sends to the GPU, and a new command buffer starts at zero. The text
  shader adds it to the fragment, so a rounded clip still cuts text drawn into such a target in the right place.

- Gestures of fingers as input. A host hands an output a recognized gesture step with
  `UniverseOutput.OnGestureInput` (`GestureInput`: translation, scale and rotation).
  - **Reading it.** `InputWormhole` adds up a frame's steps: `GesturePan`, `GesturePinch` (in doublings) and
    `GestureRotation` (in degrees). They are taken while the output takes the pointer.
  - **Binding it.** `GestureAxis` (`PanX`, `PanY`, `Pinch`, `Rotate`) is a control like the mouse's axes and the
    gamepad's: `InputControl.Gesture`, written as `Gesture:Pinch`.
  - **The camera.** Two fingers moved side by side turn the camera as the mouse with its right button does (`Look`). A
    pinch zooms as the wheel does, four notches a doubling (`Zoom`). A turn of the fingers rolls the camera by the
    same angle (`Twist`, a new action). All three can be rebound in `CameraActions`.

- Windows pointer input in `Adamantium.Win32`, for touch and pen:
  - the `WM_POINTER*` messages in `WindowMessages` and `Messages.GetPointerId`;
  - `Win32Interop.GetPointerType`, `GetPointerInfo`, `GetPointerTouchInfo` and `GetPointerPenInfo`, with
    `POINTER_INFO`, `POINTER_TOUCH_INFO`, `POINTER_PEN_INFO` and their flags and masks;
  - `SetWindowFeedbackSetting` to switch off the system's touch and pen visuals for a window;
  - `CreateSyntheticPointerDevice`, `InjectSyntheticPointerInput` (`POINTER_TYPE_INFO`) and
    `DestroySyntheticPointerDevice`, which raise real `WM_POINTER` messages without the hardware.

- `TextLayout.Update(device, window)`: builds the quads of only the glyphs that touch a window in the layout's units,
  and asks the atlas for only theirs, so a long text shown through a viewport costs what the viewport shows;
  `QuadWindow` tells which window the last build was for (`RefreshGlyphs` keeps it), `GlyphCount` how many glyphs the
  text takes, drawn or not. `BuildGlyphRun(device, window)` builds and freezes them in one step, so two callers building
  different windows of one layout on different threads never freeze each other's.

- `IGraphicsDevice.WithholdFrame`: a frame asked to be held back before `EndDraw` acquires no swapchain image, so nothing
  is submitted or presented and the window keeps showing the last frame - for a frame drawn incomplete that the next
  one draws whole. A render-target presenter keeps its frame.

- Text decoration lines, as WPF's: `TextDecorations.Overline` at the font's ascender, and
  `TextAttributes.DecorationLines` - lines each with their own place (`TextDecorationLocation`: under, over, through
  the text or on the baseline), thickness, offset, color and dashes (`TextAdornment.Dashes`), drawn besides the
  decorations; a thicker line grows away from the letters, a thicker strikethrough stays centered.

- Baseline alignment, as WPF's: `TextAttributes.BaselineAlignment` sets a range where the layout's font sets a
  superscript or a subscript (`IFont.SuperscriptYOffset`/`SubscriptYOffset`, from 'OS/2'), at the font's ascender or
  descender, or at the top, middle or bottom of its line, its size and the line's height unchanged. A baseline shift
  adds to it; its lines and hyphens follow it.

- Line height, line stacking, line spacing and the most lines on `TextLayout`, as WPF's and Avalonia's:
  `LineHeight` sets the distance between baselines, its extra room (or what it lacks) split above and below the text;
  `LineStacking` says whether a line holding text of a larger size or a taller object still grows (`MaxHeight`, the
  default) or every line is exactly that high (`BlockLineHeight`); `LineSpacing` adds space between lines; `MaxLines`
  lays out that many lines, the rest reported by `OversetIndex`, or, with trimming, the last ends in an ellipsis.
  Frames, drop caps, wrapping by symbols, hyphenation and the caret follow them.

- Objects in a line of text, as WPF's `InlineUIContainer`: `TextAttributes.ObjectSize` on a U+FFFC OBJECT REPLACEMENT
  CHARACTER sets it as an object of that size. It advances the line by its width, stands on the baseline (a baseline
  shift raises it), raises a line it is taller than, wraps and aligns as a word does and draws nothing itself;
  `TextLayout.GetInlineObjects` tells where each one stands, for the caller to draw it there. Horizontal text only.

- U+2028 LINE SEPARATOR in laid-out text ends the line but not the paragraph, as WPF's `LineBreak`: the paragraph
  keeps its direction across it, a justified line ending in it is not stretched, it draws nothing, and the caret stands
  at the end of one line and the start of the next.

- Baseline shift, as InDesign's: `TextAttributes.BaselineShift` raises a range above its line's baseline (negative
  lowers it), its size and the line's height unchanged. Its underline, strikethrough and squiggle, the hyphens of its
  words, its tab leaders and a drop cap follow it; its background stays on the line. In vertical text an upright
  glyph moves across the line, to the right.

- `TextLayout.Revision` grows each time the text is laid out anew, so a renderer keeping one layout can tell its
  glyphs changed when its text and size did not.

- Kashida justification. A justified line of Arabic and other joined scripts whose spaces are at their maximum
  stretches its words with kashidas (`TextLayout.Kashidas`, on by default): in each word, a stretched extender
  between two joined letters, after a seen or a sad where the word has one, else nearest its end, up to an em. The
  paragraph composer counts that stretch. The extender is the glyph the font's justification table gives the script
  (`JSTF`, read for its extender glyphs: `IFont.GetJustificationExtenders`), else its tatweel (U+0640), or the
  lajanyalan for N'Ko; a kashida joins only two letters that follow one another in the text.
  `CursiveScripts.JoinsNext` and `Extender` tell whether a letter joins the next and what stretches its script.

- Where the last line of a justified paragraph and a justified line of a single word stand, as InDesign sets them:
  `TextLayout.LastLineAlignment` (at the start by default, centered, at the end, or justified) and
  `SingleWordJustification` (at the start by default, centered, at the end, or justified, its letters spread across
  the line). Start and end follow the paragraph's direction; the caret on an empty last line stands there too.

- Glyph scaling and justification alternates, as InDesign justifies. `TextLayout.GlyphScaling` (a `SpacingRange` of
  the glyphs' own width) draws glyphs at their desired width, scaled about their origins, marks with their letters.
  A justified line that cannot fill its width within its spacing first takes the wider glyphs its font offers for
  justification (`jalt`, as the wide letters of Hebrew; `JustificationAlternates`, on by default), then widens or
  narrows its glyphs within the range, and only then stretches its spaces further. `GlyphWordData.HorizontalScale`
  tells each glyph's width; hyphens and the trimming ellipsis are scaled with the text. Text broken a line at a time
  and justified now also takes a word, or a hyphenated part of one, it fits by squeezing its spaces, letters and
  glyphs within their minimums, as the paragraph composer did. `jalt=0` among a range's features keeps its glyphs.

- Word and letter spacing ranges and tracking, as InDesign's justification and character settings have them.
  `TextLayout.WordSpacing` and `LetterSpacing` (`SpacingRange`: minimum, desired and maximum shares of the font's
  space; `"80% 100% 133%"` in markup) set spaces and letter spacing at their desired widths. A justified line
  squeezes and stretches its spaces within the range first, then its letters, and only past both stretches its spaces
  further. The paragraph composer weighs both: letter spacing is glue it cannot break at. `TextLayout.Tracking` and
  `TextAttributes.Tracking` add thousandths of an em after each character. Marks stay on their letters, and Arabic
  and other joined scripts are never spaced or tracked (`CursiveScripts`).

- Vertical text. `TextLayout.WritingMode` (`WritingMode.VerticalRightToLeft`) runs lines down, as long as the text
  area is high, and stacks them from its right: ideographs and kana stand upright, shaped top to bottom in their
  vertical forms and centered on the line, as do brackets and long marks the font has vertical forms of (UAX #50 `Tr`);
  other text lies turned 90° clockwise, its em centered on the line; a run of
  one or two digits among them stands upright across the line in one em (tate-chū-yoko), narrowed to fit. Wrapping,
  hyphenation, the paragraph composer, tab stops, trimming, alignment along the line, frames and exclusions work as
  across; `GetRangeRects` and `GetAdornments` give rectangles down the lines (underlines on their right,
  strikethroughs down their middle), and `GlyphWordData.Upright` and
  `Sideways` say how each glyph stands. Drop caps, carets and hit testing are for horizontal text only. A glyph's
  `FontItem.Rotation` now turns a color glyph drawn by its paint program as well.
- Shaping top to bottom, as HarfBuzz does it: `ShapingOptions.Vertical` applies the 'vert' forms (found anywhere in
  the font when the script lacks them), leaves out the horizontal features (`kern`, `liga`, `calt`…), advances each
  glyph down by its 'vmtx' advance and offsets it from its vertical origin ('VORG', else its top plus its 'vmtx' top
  side bearing, for TrueType outlines; the font's ascender otherwise); without a 'vert' feature it falls back to
  Unicode's vertical presentation forms. GPOS values move the pen down in vertical text (`vpal`, `vkrn`), and
  Arabic-family scripts do not join in it, as in HarfBuzz. Checked against HarfBuzz on Noto Sans CJK (Japanese, kana,
  brackets and punctuation, `vpal`, Korean, Latin), on fonts without vertical metrics, on CFF Latin and on Arabic. `IFont.HasVerticalMetrics`, `GetAdvanceHeight` and `GetVerticalOriginY` give the metrics;
  `VerticalOrientations` gives the Vertical_Orientation of characters (UAX #50, Unicode 16.0): which stand upright in
  vertical text and which lie turned.
- Text frames. `TextLayout.Frames` flows text wrapped by words through rectangles in turn - columns side by side, or
  frames anywhere, as InDesign threads them - each holding as many lines as its height takes; what none has room for
  is not laid out, and `OversetIndex` tells where it starts. `TextLayout.Exclusions` are areas the text flows around:
  a line runs in the widest part of its frame they leave free, and a row they leave too little of is skipped. Lines
  break, hyphenate, justify and align within their own frame and width, a line at a time or a paragraph at a time;
  a drop cap stands at the start of the first frame; a click finds the line of the frame it falls in.
- Drop caps. `TextLayout.DropCap` (`DropCap`: how many lines, how many characters, an optional decorative font) sets
  the first characters of text wrapped by words large across its first lines, from the cap height of the first to
  the baseline of the last, the lines beside it indented by its width - a line at a time and a paragraph at a time.
  `ParagraphComposer.Break` and `BreakAnyway` take a width for each line in turn, as TeX does, keeping nodes of
  different lines apart while the widths differ; checked against every breaking of random paragraphs with narrower
  first lines.
- Tab stops. `TextLayout.TabStops` (`TabStop`: a position, a `TabAlignment` - `Left`, `Center`, `Right` or
  `Decimal` on its `AlignOn` character - and a leader) moves the text after a tab to the next stop; leaders repeat
  across the gap on one grid, so they line up from line to line. Past the last stop, tabs stop every `TabSize`
  spaces as before.
- Optical margin alignment. `TextLayout.OpticalMarginAlignment` hangs punctuation, quotes and hyphens at the edges of
  lines partly past the margin - left for left-aligned and justified lines, right for right-aligned and justified
  ones - so the edge looks straight, as InDesign's Optical Margin Alignment does.
- Japanese line breaking is checked against the kinsoku rules of JIS X 4051 (closing brackets, small kana, the
  prolonged sound mark and full stops never start a line, opening brackets never end one), a line at a time and a
  paragraph at a time.
- The paragraph composer. `TextLayout.LineBreaking` (`LineBreaking`: `Greedy` - the default, a line at a time - or
  `Paragraph`) breaks text wrapped by `WrapByWords` a paragraph at a time, as TeX and InDesign's Paragraph Composer
  do. `ParagraphComposer` is Knuth and Plass's algorithm over boxes, glue and penalties, with TeX's badness, fitness
  classes and demerits (line penalty 10, a hyphen after a hyphen 10000, a hyphenated next to last line 5000, fitness
  classes two apart 10000), and its three passes: without automatic hyphenation at tolerance 100, with it at 200,
  then with an emergency stretch, where a word wider than the line overflows. Justified text gets spaces that
  stretch by half and shrink by a third; ragged text an even edge, with a stretch of 2em at the end of each line.
  Checked against every breaking of 400 random paragraphs; a paragraph's demerits are never above those of the same
  text broken a line at a time.
- Justified lines shrink their spaces, by up to a third, when the composer set them tighter than their natural width.
- Hyphenation. `TextLayout.Hyphens` (`Hyphens`: `None`, `Manual` - the default - and `Auto`) lets a word wrapped by
  `WrapByWords` break across lines, a hyphen (U+2010, or `-` when the font lacks it) ending the line: at its soft
  hyphens (U+00AD, drawn only where a line breaks at one), and under `Auto`, in a word without any, where the
  hyphenation patterns of its language allow. `Hyphenator` runs Liang's algorithm over TeX's patterns from hyph-utf8,
  with their exception lists and the letters each language keeps at each side of a break; built in are Russian,
  English as the United States and as Britain write it, German (1996 orthography), French and Spanish, and
  `Hyphenator.Register` adds others (`Hyphenator.Load` reads any hyph-utf8 `.tex` file). The language is the run's
  `TextAttributes.Language`, else the new `TextLayout.Language`, which also picks the font's localized forms and the
  fallback fonts for plain text. Checked against the `hyphen` package, another implementation of the algorithm, on
  132 words over the six pattern files (`Tests/Adamantium.HyphenationOracle`).
- `TextTrimming` with `WrapByWords`: a word too wide for its line, which hyphenation cannot break, ends in an ellipsis
  and the text goes on below it; the last line the height leaves room for ends in one when text is left over, after a
  word or a newline (`WordEllipses` drops the word that does not fit, or cuts by characters on a line without spaces). Trimming used to apply only without wrapping or by characters.
- Arabic shaping, as HarfBuzz's Arabic shaper does it: letters take their joined forms (`isol`, `fina`, `fin2`,
  `fin3`, `medi`, `med2`, `init`, by the joining types of `ArabicShaping.txt`), the features apply in their stages
  (`ccmp`/`locl`, each form, `rlig`, `calt`, `mset`), and modifier marks such as hamza go first among the marks of
  their class (UTR #53). The same applies to Syriac, N'Ko, traditional Mongolian, Mandaic, Adlam and the other joining
  scripts. Checked against HarfBuzz on Noto Sans Arabic, Noto Naskh Arabic and Noto Nastaliq Urdu (Persian, Urdu,
  vowel marks, lam-alef, tatweel, ZWJ and ZWNJ). Not yet: the stretching of Syriac abbreviation marks (`stch` is
  applied, but its glyphs are not repeated across the word), and shaping from Unicode presentation forms for a font
  without Arabic GSUB. `TextShaper.Shape(font, text, start, end, options)` shapes a range of a string as if alone (its
  clusters and feature ranges count from `start`) with the text around it as context, so letters join across the runs
  `TextLayout` cuts a word into.
- Church Slavonic and Old Cyrillic checked against HarfBuzz on Ponomar Unicode and Monomakh Unicode: breathings,
  accents and titla stacked over letters, letter-titla, numbers under the titlo, the old letters (yuses, yat, fita,
  izhitsa, ksi, psi, koppa). They shape as HarfBuzz does with no change to the shaper.

- Text in both directions, by the Unicode Bidirectional Algorithm (UAX #9, Unicode 16.0; every case of `BidiTest.txt`
  and `BidiCharacterTest.txt` passes). `TextLayout.Direction` (`TextDirection`: `Auto` takes each paragraph's
  direction from its first strong letter) lays Hebrew and other right-to-left runs out right to left, numbers and
  Latin among them left to right, and each line in visual order after it breaks. `BidiParagraph` gives the levels of
  a paragraph. The shaper shapes right to left as HarfBuzz does: mirrored brackets (`rtlm`, `rtla`), cursive
  attachment and marks the other way, HarfBuzz's combining classes for Hebrew marks; checked against HarfBuzz on
  Noto Sans Hebrew.
- Caret and selection for text in both directions. `CaretStop.IsRightToLeft`, `Left`, `Right` and `After` (the caret
  before a right-to-left letter stands on its right edge, after it on its left). `CaretPosition` (before a character,
  or after the one before it: two places where the direction changes), `TextLayout.GetCaretPoint`, `MoveVisually`
  (one letter left or right on screen, over invisible marks), `GetLineStart` and `GetLineEnd` (the line's edges on
  screen; the end is before the newline or at the end of the text, so typing there appends), and selection by the
  screen: `GetVisualRanges` (the characters between two points on screen, which may lie apart in the text).
  `IsRightToLeftParagraph`. A paragraph separator inside a line starts a paragraph of its own.

- A variable font on the way between two sets of axis values, as an animation passes it:
  `IFont.GetInstance(from, to, progress)` lays text out at exactly the values there, and its `Blend` names the two key
  instances around it, set at every eighteenth of the axis that moves most (wght 100, 150, 200...) so animations share
  them. `TextLayout` draws such a glyph from both keys' cells (`FontItem.SecondSource`, `Second`) and `FontEffect`
  blends their true distance; the point's own glyphs never reach the atlas. Color glyphs are drawn as the nearer key.
- The rest of a variable font's axes. `FontAxis.Name` and `IsHidden` (the font's name for an axis, and whether it
  keeps out of style panels); `IFont.NamedInstances` (the styles 'fvar' names, as "Bold Condensed");
  `IFont.AxisValues` and `ElidedFallbackName` from the style attributes table ('STAT': "Light" on `wght`, "Condensed"
  on `wdth`, which ones a style's name leaves out). 'MVAR' varies a font's line, cap height, underline and strikeout
  metrics; 'FeatureVariations' swaps the lookups a feature applies at the axis values it names (heavy `$` in Roboto
  Flex), and kerning and mark positions take their deltas from GDEF's variation store. `IFont.AtOpticalSize` sets
  'opsz' to the size of the text, as CSS's `font-optical-sizing: auto`, unless it was asked for by value, and
  `TextLayout` sets each run's; `FontVariation.For` with a style sets 'ital' or 'slnt' as CSS Fonts 4 does, and
  `FontCollection.Load` takes the style. Axes, named instances, metrics and shaping at axis values are checked
  against HarfBuzz.

- What a panel of OpenType features and a glyph panel need from a font: `Feature.Name` (a stylistic set's or a
  character variant's own label from the 'name' table, otherwise the registered name), `Tooltip`, `SampleText`,
  `ParameterLabels`, `Characters` and `ValueCount` (how many alternates the feature offers); `IFont.GetName` for any
  string of the 'name' table; `IFont.GetGlyphAlternates` - every glyph a single or alternate substitution turns a glyph
  into, with the feature and value that ask for it; `IFont.GetGlyphText` - the characters a glyph stands for, through
  substitutions and ligatures. Checked against HarfBuzz.
- `TextShaper.DefaultFeatures`: the features shaping applies without being asked (`ccmp`, `locl`, `calt`, `liga`,
  `kern`…), from the same list the shaper plans with, so a settings panel can show a font's `calt` as on and its
  `ss01` as off before anything is asked for.
- `FontCollection`: fonts grouped into families from their headers alone (`name`, `OS/2`, `head`, `fvar`; collection
  files included), found by typographic, older or full name, with each face's weight, slant and width (`FontFace`).
  `Match` picks a face as CSS Fonts 4 and the browsers do: the width first, then the slant, then the weight; a variable
  font covers its range. `FontCollection.System` indexes the operating system's fonts once and keeps the index on
  disk, reading again only files changed since. `FontWeight` (1-1000, `SemiBold`, `650`), `FontStyle` and
  `FontStretch` (`Condensed`, `75%`). `Typeface.LoadSystemFont` finds a font through it instead of parsing every file's
  names.
- `TextAttributes.Font`: a face per range, such as a bold word in a regular line; each line takes the largest ascent
  and descent of its fonts. `GlyphWordData.Font`, `TextLayout.GetGlyphs`.
- `TypeParser` parses a type that has its own public static `Parse(string)` without a registered parser.
- Font fallback: a character the text's font lacks is drawn from another font, in the weight, slant and width of the
  text's (`FontFallback`, `TextLayout.Fallback`; the operating system's fallback families unless set, none with null).
  Han, kana and Hangul take the family of the text's language first (Japanese, Korean, Traditional or Simplified
  Chinese), emoji the emoji family; a grapheme (with its marks, joiners, variation selectors and emoji modifiers)
  stays in one font. A family's character map is read without loading the font (`FontFace.HasCharacter`), so only
  the font chosen is loaded. A character no font has draws the font's missing-glyph box.
- `IFont.Weight`, `Style` and `Stretch` from 'OS/2'; `IFont.GetAdvanceWidth` and `GetLeftSideBearing` from the font's own
  'hmtx'; `IFont.TryGetGlyphIndex` is public.
- `GrowingTextureArray`: a 2D texture array that grows as its layers are taken, the way a list does: `Count`,
  `Capacity`, `MaxCapacity` (the device's limit), `TryAdd`, `EnsureCapacity`; a full array is replaced by one twice as
  deep with the layers in use copied on the GPU, and the old texture is retired once no frame in flight reads it.
  `ITexture.ReadbackToImage` takes the layer to read.
- `Win32Interop.LoadImage` and `GetModuleHandle`, with `LoadImageType` and `LoadImageFlags`: an icon loaded from a
  module's resources, such as the application's own.
- `Win32Interop.EnumDisplayMonitors`, `GetMonitorInfo` and `GetWindowPlacement`, with `MONITORINFOEX`,
  `WINDOWPLACEMENT` and `MonitorEnumProc`: the monitors and their work areas, and a window's restored rectangle.
- `TextShaper.Shape` in `Adamantium.Fonts.Shaping`: text to positioned glyphs with the font's OpenType substitutions and
  positioning applied the way HarfBuzz applies them - lookups of every enabled feature in the font's order, lookup
  flags and mark filtering sets, ligatures across marks, mark attachment to bases, ligatures and other marks, cursive
  attachment, contextual and chained rules of every format. Features are set per range of text (`FontFeature`,
  parsed from `liga=0`, `ss01`, `kern[3:5]=0`), the script and language pick the font's language system, combining
  marks are reordered and composed when the font has the precomposed glyph, and each glyph keeps the UTF-16 offset of
  the text it came from.
- `TextLayout.GetCaretStops` and `CaretStop`: the caret position before each character and after the last, with the
  character's width; the characters of one glyph, such as a ligature, share its width. `GlyphWordData` carries its
  `PenX`, `Advance` and positioning offsets; `FontAtlas.RequestAsync` takes glyphs as well as text.
- `AttributedText` and `TextAttributes`: text with features, language, color, background and lines
  (`TextDecorations`: underline, strikethrough, squiggle) over ranges, laid out by `TextLayout.ProcessText`. Each glyph
  takes the color of its text on both text paths, a glyph without one the element's (`FontItem.HasOwnColor`).
  `GetAdornments` gives the backgrounds and lines to draw, `GetRangeRects` the rectangles of a range line by line (for a
  selection), and `GetLine` / `LineCount` the visual lines with their top and baseline. The fonts' underline and
  strikeout metrics are read (`IFont.UnderlinePosition`, `UnderlineThickness`, `StrikeoutPosition`, `StrikeoutSize`).
- `TextShaper.Shape` without a script splits the text where its script changes and shapes each part with its own, so
  Latin and Cyrillic in one line each get their own forms.
- `TextBoundaries` in `Adamantium.Fonts.Text`: grapheme and word boundaries by Unicode Standard Annex #29, emoji
  sequences, flags and Indic conjuncts included; it passes all of Unicode's `GraphemeBreakTest` and `WordBreakTest`.
  `TextLayout` uses them: `NextCaretStop` / `PreviousCaretStop` step over a whole grapheme, `HitTest` gives the grapheme
  under a point and where a click there puts the caret (`TextHit`), and `GetWordAt`, `NextWordStop` and
  `PreviousWordStop` give words for a double click and Ctrl+arrows.
- `TextBoundaries.LineBreaks`: where a line may or must end, by Unicode Standard Annex #14; it passes all of Unicode's
  `LineBreakTest`.
- Typed font features: `FontFeature.Ligatures`, `Capitals`, `Numerals`, `Position` and `Kerning` (`FontFeature.Ligatures.Off`,
  `FontFeature.Numerals.Tabular`), `StylisticSet(n)`, `CharacterVariant(n, value)`, `StylisticAlternates` and `Swash`.
  `FontFeature.TryParseList` checks a list such as `liga=0, ss01, cv05=2` against the OpenType feature registry: a
  lowercase tag the registry does not have is an error naming the tag most likely meant, and a private feature needs an
  uppercase letter in its tag. `ParseList` throws with the same message.
- `TextAttributes.FontSize`: a size per range of text. Each line is as tall as its largest text and its glyphs share its
  baseline; `GetLine`, `HitTest`, `GetRangeRects` and `GetAdornments` follow each line's own height.
  `GlyphWordData.FontSize` is the size a glyph is set at.
- `TextLayout.TabSize`: a tab moves the pen to the next tab stop, a multiple of that many spaces from the line's start
  (4 by default), and draws nothing. Before, it was laid out as the font's glyph for it, usually the `.notdef` box.
- Variable fonts: `IFont.Axes` (`FontAxis`: tag and range, from 'fvar') and `IFont.GetInstance`, the font at the axis
  values given (`FontVariation`, `wght=650`), with its own outlines and advances: TrueType outlines moved by 'gvar'
  (untouched points interpolated, composite offsets and phantom points varied), CFF2 outlines blended, advances from
  'HVAR' or the phantom points, coordinates mapped through 'avar'. An instance has its own `Typeface`, so its glyphs
  take their own places in the shared atlas; the same values give the same instance. Advances match HarfBuzz exactly
  and outline extents to a unit on Source Sans 3 VF (TrueType and CFF2), Bahnschrift, Segoe UI Variable and Sitka.
  `FontCollection.Load(face, weight, stretch)` sets a variable face's 'wght' and 'wdth' (`FontVariation.For`), and
  fallback fonts take the weight and width of the text's.
- Fonts load without waiting: `TypefaceStore.TryGetTypeface` and `LoadInBackground` (parsed on a worker, which raises
  `TypefaceStore.Loaded`; `LoadedCount`), `FontCollection.TryLoad`, `FontFace.TryHasCharacter`, and
  `FontFallback.FontFor(codepoint, like, language, out pending)`. With `TextLayout.LoadFontsInBackground` a character
  whose fallback font is still loading takes the room of the missing glyph and draws nothing; `HasPendingFonts` says
  to lay the text out again when the font arrives.
- `TextureAtlasGenerator.GenerateTextureForGlyphs(glyphs, ready)` hands each glyph over as soon as it is rasterized.
- Color glyphs from 'COLR' version 0 and 'CPAL': a color glyph is drawn as its layers, bottom first, each an ordinary
  glyph of its font in a color of the font's first palette, or in the text's color for the foreground entry
  (`IFont.GetColorLayers`, `ColorLayer`). The layers take places in the shared atlas and draw in the same batch as the
  text around them, so Segoe UI Emoji's emoji come out in color at any size.
- 'COLR' version 1 paint graphs read as the steps that draw them (`IFont.GetColorPaint`, `ColorPaintOperation`,
  `ColorPaintOperationKind`, `ColorCompositeMode`, `ColorFill`, `ColorStop`): clips by glyph outlines, groups
  composited with any of the format's 28 modes, and fills with a color or a linear, radial or sweep gradient,
  transforms folded in. Checked step for step against HarfBuzz on Google's COLRv1 test fonts.
- Color glyphs from 'COLR' version 1 drawn in real time: a color glyph is one quad whose pixel shader runs the glyph's
  paint program - nested clips intersect, groups composite exactly (Porter-Duff, blend and HSL modes), linear, radial
  (two-point conical) and sweep gradients with pad, repeat and reflect, interpolated premultiplied - each outline from
  the atlas's distance field, transformed. The atlas keeps one program per glyph in font units, so one serves every size
  (`FontItem.Paint`; FontEffect's `PaintProgramsAddress` and `PaintStopsAddress`). Segoe UI Emoji's emoji come out
  with their gradients, shading and translucent groups, as Edge draws them; the 28 modes of Google's test font too.
- Color glyphs from PNG images, 'CBDT'/'CBLC' (Noto Color Emoji) and 'sbix': `IFont.ColorBitmapSizes`,
  `IFont.GetColorBitmap` (`ColorBitmap`: the PNG and where it lies, in pixels of its size), the size chosen as HarfBuzz
  chooses it and checked against HarfBuzz's PNGs and extents; a font with images and no outlines loads. Such a glyph is
  one quad of its paint program, whose one step samples a color atlas of its own: premultiplied cells sized to the
  image by powers of two (Noto's 136×128 takes 256×128), each with its own mips, read trilinear at the level the size
  asks for and kept inside the cell. Images are decoded on a worker and land as outline glyphs do. Vector color
  ('COLR') wins over images where a font has both.
- 'COLR' version 1 at a variable font's axis values: every variable paint takes its deltas (the variation store, through
  the delta-set index map) - solid colors' alpha, gradients' points, radii and angles, color stops' offsets and alpha,
  and every transform. The clip list is read (`IFont.TryGetColorClipBox`, its boxes varying too) and a color glyph is
  drawn within its box. Checked against HarfBuzz on Google's variable test font, an axis of each kind set.
- Color glyphs from SVG documents ('SVG ', plain or gzipped): a glyph's drawing becomes the steps a 'COLR' version 1
  glyph is drawn with, so it draws in real time at any size - each shape (path with its arcs, rect, circle, ellipse,
  polygon) an outline clipping its fill, a solid color or a linear or radial gradient (bounding-box or user units, its
  transform, pad, reflect or repeat, stops' opacity, one gradient taking what it lacks from another), groups with their
  transforms and their opacity fading the group as a whole, `use`, `clip-path`, `fill-rule="evenodd"`, `currentColor`
  as the text's color and `var(--colorN)` from the text's palette. Strokes, masks, patterns, filters and style sheets
  are not drawn. Google's samples drawn from their SVG documents match the same drawings
  drawn from their 'COLR' version 1 graph.
- `SvgPathData` in `Adamantium.Mathematics.Svg`: `Walk` reads SVG path data into an `ISvgPathSink` as absolute steps
  (relative points, `H`/`V`, smooth curves' reflected controls and repeated commands worked out; arc flags written
  without separators included), `ArcToCubics` turns an arc into cubic curves, and `ReadNumbers` reads SVG's numbers;
  `Colors.TryGetNamed`.
- Color palettes: `TextAttributes.ColorPalette` draws a range's color glyphs in another of its font's 'CPAL' palettes
  (`IFont.ColorPalettes`, `ColorPalette` with the backgrounds version 1 says it suits, `ColorPaletteUsage`;
  `IFont.GetColorPaint` and `GetColorLayers` with a palette); a palette the font lacks draws in its first. Checked
  against HarfBuzz's paint in the second and third palettes of Google's test font.
- Emoji sequences: a variation sequence the font maps ('cmap' format 14) shapes to its own glyph, its selector dropped,
  as HarfBuzz does (`IFont.TryGetGlyphIndex(codepoint, variationSelector, out glyph)`), and a default-ignorable
  character the font's substitutions have replaced is no longer hidden or zeroed: a keycap, a joined flag, a skin tone
  and the ideographic variants of Noto Sans CJK shape as HarfBuzz shapes them. A grapheme takes one font, the one its
  first character picks; one drawn as an emoji (`EmojiPresentation.IsEmoji`: VS16 after an emoji character, a flag, a
  skin tone, a joined sequence, or an emoji by default) takes the emoji family unless the text's font has the glyph in
  color, keeping the text's font when no family has it in color; one drawn as text (VS15, or text by default, as a bare
  heart) does not prefer it (`FontFallback.FontFor(..., emoji)`). Segoe UI Emoji's sequences come out as Edge draws
  them.
- Synthesized bold and italic for a face a family lacks (`FontSynthesis`, `TextAttributes.Synthesis`): a bold moves
  the glyph's edge out in the distance field, by a 48th of the size per side at 9 pixels down to a 64th at 36, and
  advances it further by twice that; an italic slants the glyph's quad about its baseline by a quarter of its height
  (about 14 degrees), and the text takes the room its letters lean into. Both draw from the same atlas glyphs, in the
  same batch as the text around them (`FontItem.Synthesis`). `FontSynthesisRules.Needed` says what to synthesize: a
  weight of 600 or more on a lighter face, an italic or oblique style on an upright one, only as allowed.

### Fixed

- Text aligned right threw when a line held only spaces: leading spaces before a word too long to follow them on the
  first line. A line with nothing to draw is left where it is.

- A retired GPU resource waited for every render device to begin new frames, so one device that stopped drawing - an
  off-screen one between two bakes - kept every resource retired after it, and everything those referenced, for the
  life of the application. A device with no frame in flight (`IGraphicsDevice.HasNoFramesInFlight`) no longer holds
  them back; one in the middle of recording a frame still does.

- Decoding an animated GIF threw away about a gigabyte for a 200-frame 960x540 animation: the LZW decoder grew a list
  for every frame and a list for every code, every frame kept its index stream until all of them were done, and every
  frame drawn over a part of the one before copied the whole picture's indices first. The decoder writes each frame's
  indices once, at their final size, the newest frame's stream is the only one kept, and the next frame is laid over
  it in place. The pixels are the same, to the byte. `IRawBitmap.CopyFrame` writes a frame into the caller's buffer
  without the decoder keeping it, so an animation read frame by frame (`GifImage` does it in one pass) needs one
  frame's worth of memory instead of all of them.

- `THIRD-PARTY-NOTICES.md` lists `EmojiPresentation.ucd` among the Unicode data `Adamantium.Fonts` embeds. The
  `.ucd` files are checked out with LF, the line endings they are written with, so git no longer shows them changed
  once a tool has touched them.
- Justified text: a widened space now takes its added width into its advance, so the caret, a click and a selection
  reach across the gap instead of skipping it; justified right-to-left lines run from edge to edge, the trailing
  space that ends up on their left no longer holding them off it, and the last line of a right-to-left paragraph
  stands at its start, on the right.
- A paragraph broken a paragraph at a time put a word the composer hyphenated at a line's end whole onto the next
  line, its head left alone there ("тек-" on a line of its own): the line now takes the head, as the composer chose.
- The system fallback drew Chinese and Japanese punctuation and full-width forms (、。「」（）Ａ) from the first family
  that had them - Mongolian Baiti on Windows - instead of the family the text's language reads them in; they now try
  it first, as Han and kana do.
- 'vmtx' was read with the horizontal metrics' count (`hhea`) instead of its own (`vhea`), and before `vhea`, so the
  vertical advances of most CJK fonts were wrong; `vhea` also overwrote the line spacing `hhea` gives.
- `Justify` stretched the last line of every paragraph but the text's last, a line ending at a newline; it stays as it
  is now, unless `justifyLastLine` asks otherwise.
- `WrapByWords` measured a word by the sum of its glyphs' ink, leaving out the gaps between them, so a word wider than
  the room left could stay on the line and run past the edge. It is measured from the pen to the right edge of its
  ink now.
- `WrapByWords` took a newline and the word after it for part of the word before, so `"aa Hello\nworld"` measured
  "Hello" together with "world" and could move it to a line of its own. A word ends at a newline now.
- Specks beside a glyph where its outline has a corner twice, a hair apart, as a variable font's deltas can leave it
  (Bahnschrift's "3" at weight 380): cutting the outline where it crosses itself made near copies of corners, so a
  corner looked like two loose ends and the outside leaked in along a row of texels. Cuts that fall on a corner are
  not made, and the distance field joins ends nearer than a thousandth of a unit and drops segments that short.
- Three layout tables were read wrong, so their lookups did nothing or put marks far off: the rules of a contextual
  lookup of format 1 or 2 (read without their offsets and with an empty input sequence), the anchors of a
  mark-to-mark lookup after its first mark, and the anchors of a mark-to-ligature lookup (taken from the wrong table).
  Vowel marks stacked on Arabic letters, on lam-alef and on Allah, and the contextual forms of Nastaliq, come out as
  HarfBuzz's now.
- A variable font's 'STAT' and 'MVAR' tables were never read: the parser keeps only the tables its ordering list names,
  and neither was on it. GPOS's 'FeatureVariations' offset was read as 16 bits instead of 32.
- A varied advance, a font metric and a positioning delta that land on a half round up, as HarfBuzz rounds them; they
  rounded away from zero, a unit off for a negative half.

- A render device's per-frame constants are rewound when that device begins its own frame, not when the main device's
  frame finishes. A device recording beside the window's loop - an off-screen render - had its pool rewound mid-frame,
  and its later draws overwrote the constants of its earlier ones.
- `TransitionImageLayout` moves every mip level, not only the first: a texture made with mip levels left the rest
  undefined while it said they were in its layout, and the validation layer reported the first barrier that relied on
  it.
- `IFont.GetInstance` gave the font itself for axis values when its outlines do not vary ('glyf' without 'gvar'), so
  what else varies - advances ('HVAR'), color glyphs ('COLR') - stayed at the defaults. Such a font now has its
  instances, their outlines read as they are.
- A 'COLR' version 1 outline drawn through a transform that squeezes one axis (Google's samples draw a vertical bar
  from a horizontal one scaled by 1/8 and 8) lost its antialiasing along that axis and came out half a pixel narrow: the
  field's distance became pixels by both axes' average. It becomes pixels along the outline's normal.
- A palette PNG (color type 3) did not decode: the palette was read by its byte count as if by its entries and ran
  past the chunk. It reads entry by entry, and an index past the palette is an error rather than a stray read.
- `GrowingTextureArray` copied only level 0 when it grew; an array with mips keeps every level.
- A glyph's distance field drew a faint line past an acute corner, along the extension of one of its edges, out to the
  edge of its cell, and dots in narrow gaps (the swirls of Segoe UI Emoji's ice cream; 37 glyphs of Segoe UI, 12 of
  Source Sans 3, 30 of Cascadia Code): neighboring texels' channels, interpolated, put the contour where it is not.
  Where they do, a texel past one from the outline takes the true distance in every channel, as does every texel past
  three; corners keep their channels, sharp for a bold that moves the contour out.
- A point on the line of a corner's edge, beyond the corner, could take the distance to the glyph's outline with the
  wrong sign, drawing a dot well outside the glyph (Cascadia Code's geometric shapes, such as ◆): the two edges at the corner
  were equally near but for the last digits, and only one was kept.
- `FeatureInfo.DefaultFeatureState` said Off for features shaping applies to all text unasked (`liga`, `kern`,
  `calt`, `locl`, `mark`…) and On for `size`, which shaping never applies. It comes from the shaper's own list now
  (`TextShaper.DefaultFeatures`), and `FeatureInfo.Create` no longer takes a default state.
- A text block drew no more than 4096 glyphs and dropped the rest without a word; with a quad per layer of a color
  glyph, some 65 emoji filled it. `TextLayout` has no cap now: its glyphs and the direct path's vertex buffer
  (`EnsureVertexBuffer`) grow to what the block holds.
- A line holding nothing but text of another font (a fallback emoji, a run in a smaller font) took that font's height
  and baseline: a line of emoji sat 4 pixels higher than its neighbours. A line starts from the text's own font, as a
  CSS line box starts from its strut, and only grows for taller text.
- The fonts of a collection file after the first one skipped every table they share with it, their character maps
  included, so NSimSun of simsun.ttc had no characters at all. Only outline data (`glyf`, `loca`, `CFF`) is shared now;
  each font reads its own character map, metrics, names and layout tables, and its advances and bearings come from
  its own 'hmtx' instead of the glyphs the fonts share.
- A `cmap` of format 13 (a range of characters to one glyph, as last-resort fonts map) failed the font's loading, and
  its lookup found only the first character of a range.
- Letters drawn from overlapping contours lost pieces: in Cascadia Code and Mono `n h m u a r 3 4 5` were cut and `w`
  vanished. Variable fonts and the static fonts made from them draw a stem and an arch as separate contours, and the
  overlaps were removed by flipping an "inside" flag at each crossing. A contour piece is now kept where the glyph is
  filled on one side of it and empty on the other, by the nonzero rule; an edge two contours share is kept once.
- One glyph that could not be rasterized dropped every glyph of its batch, so all the text asking for letters in that
  frame drew blank. `TextureAtlasGenerator` now leaves that glyph out, as one without an outline, and names it in
  `Typeface.ErrorMessages`; the rest of the batch lands.
- A font's names came from whichever record of its `name` table came last, often a translation: Segoe UI Bold was
  "Segoe UI Gras". The English (United States) Windows record is taken first, then any Windows one, then Unicode, then
  Mac Roman, which is read as single-byte text instead of UTF-16. `TypefaceStore` is safe to use from several threads.
- Thin slivers of other letters at the edges of glyphs, mostly at fractional sizes. Atlas cells lie edge to edge and a
  glyph's quad reached the very edge of its cell, so the texture filter mixed in the neighboring cell's field. The quad
  now stops half a texel inside its cell.
- Text in a second font drew the first font's letters. `FontAtlasStore` kept one atlas per set of rasterization
  parameters, whatever the font, and glyphs are found in it by index, so a glyph of the second font got the picture the
  first font has at that index. Every font now shares one atlas (32 MB of video memory each, so one per font would not
  scale) and a glyph is found in it by its typeface and index (`GlyphTextureData.Key`, `Typeface.Id`): `FontAtlas` is
  made without a font, `RequestAsync`, `GetGlyphData` and `GetUVCoordinatesForGlyph` take the font with the glyph,
  `FontAtlasStore.GetOrCreateFrom` takes only the parameters, and each glyph is rasterized at its own font's em.
  `IFont.Typeface` is public.
- A swapchain rebuild that failed - out of device memory, say - left the presenter without surfaces, and the next frame
  drew into them and crashed the process. `GraphicsPresenter.IsReady` now says whether the surfaces exist,
  `GraphicsDevice.BeginDraw` skips the frame while they do not, and the failed rebuild reports `OutOfDate`, so it is
  retried on the next frame. A retry no longer destroys the swapchain objects a second time.
- `ErrorOutOfDeviceMemory` when several applications, or an application and its designer previews, ran at once on a GPU
  without Resizable BAR. The device-local host-visible window (about 214 MB on such cards) is shared by every process;
  when it is full, buffers that want it now take host-visible system memory instead of failing.
- Letters of one height no longer jump a pixel apart within a line. `TextLayout` rounded each glyph's top and bottom to
  a whole pixel on their own, so a round letter, a fraction of a pixel above a flat one, could land a whole pixel above
  it. Only the line's baseline is rounded now; every glyph stands exactly where the font draws it.
- CFF delta arrays (`BlueValues`, `OtherBlues`, `StemSnapH` and the rest) were decoded backwards: each value was taken
  as the difference from the previous one instead of their sum.
- Every base glyph of a mark-to-base subtable got the anchors of the first one, so a mark sat where it belonged on one
  letter only. The GDEF mark glyph sets were read from the wrong offset.
- `Font.GetGlyphByIndex` returned the glyph at that position among the glyphs `cmap` maps, not the glyph with that
  index, so any glyph reached through a substitution - a ligature, a small capital - came back as another one.
- Glyphs drawn inside out in the distance field: a contour running against its format's direction, as a mirrored
  component of a composite glyph does, fills the same under the winding rule but read as a hole, so ☃ from Segoe UI
  Emoji grew wedges of fill and 😀 lost its mouth. Merged outline segments now all fill on the side their format
  fills on.
- A composite glyph whose components are placed by matching points was marked invalid and drawn empty; its components
  are placed on the points now.
- A CFF glyph's bounds took only its on-curve points and dropped the fraction, so a curve bulging past them was cut off
  at the edge of its atlas cell; they now take the control points too and are rounded outward.
- 32-bit deltas of an item variation store (long words) were read as 16-bit ones.

### Changed

- Text lays out two to three times faster and allocates a third as much, the result unchanged: a lookup skips the
  glyphs none of its subtables covers, a contextual rule allocates only for a glyph it covers, a font no longer locks
  to look up a glyph it has, the Latin range of the Unicode tables is read from an array, and centered, right-aligned
  and justified text groups its glyphs by line once instead of scanning them all for every line.

- `TextLayout` is split by stage, its public API unchanged: shaping (`GlyphRunShaper`), composing lines
  (`LineComposer`), the glyph quads (`GlyphQuadBuilder`) and caret, hit and selection queries (`TextNavigator`), over
  one immutable result per layout (`LaidOutText`) and a snapshot of the settings it was laid out with. Laid out the
  same, to the bit, across 14,688 combinations of text, script, wrapping, trimming and alignment and every special
  setting. `GetTextData` before the first layout returns no glyphs instead of throwing.

- Justified text keeps its spaces within InDesign's word spacing by default: 80% to 133% of the space. Before,
  spaces could squeeze to two thirds and stretch without limit; the paragraph composer weighed them at two thirds to
  one and a half.
- `HorizontalTextAlignment.Left` puts a line at its start and `Right` at its end, as WPF does: a right-to-left
  paragraph aligned `Left` stands on the right edge. Left-to-right text is aligned as before.
- `TextLayout.HitTest` beside a line stands on the line's edge on screen: on the side its paragraph ends, before the
  newline or at the end of the text (`IsTrailing` false); on the other, the outer edge of the outermost grapheme. The
  caret stop at the end of text that mixes directions stands where its last paragraph ends: the right edge of a
  left-to-right one, the left of a right-to-left one; so does the empty line after a right-to-left paragraph's
  newline. `GetLine` and `LineCount` no longer walk every caret stop.
- An instance of a variable font makes its glyphs as they are asked for, instead of all of them: Roboto Flex's
  instance took 1.2 ms and 720 KB (and set off collections that held frames for 15 ms), and takes 0.03 ms and 21 KB.
- A glyph's distance field takes about a third of the work it did, with the same bytes (checked on 7498 glyphs of ten
  fonts, TrueType, CFF, variable, CJK and emoji): one grid searches every channel at once, by squared distance, from
  the bounds the texel before it left; a texel far enough from the outline that its colors are replaced by the true
  distance anyway is not searched for them; and the rows of a glyph are computed in bands on several threads. One
  glyph went from 6-10 ms to 1.3-1.6 ms, a parallel atlas of Segoe UI from 2.1 s to 0.9 s.
- `Adamantium.EffectsCompiler`, `Adamantium.Engine.Generators` and `Adamantium.FX` are on `Adamantium.Vulkan.Slang`
  1.0.12: the same Slang compiler, in the package that also ships `slangd`, its language server.
- Glyphs reach the atlas one by one as they are rasterized, the heaviest started first and spread over the workers, so
  a few complex glyphs no longer hold back the rest of a batch: the sandbox's text page went from 8.6 s of glyph
  generation to 1.1 s. The distance field of a glyph looks only at the outline segments near each texel (a grid of
  them) instead of all of them, with the same result; ☃ from Segoe UI Emoji took 4.8 s and takes 0.3 s.
- `TypefaceStore` parses each file once without holding one lock for all of them: files parse in parallel, and a font
  already loaded is found while another parses.
- The font atlas grows instead of filling up: it starts with two layers (8 MB of video memory instead of 32) and doubles
  as glyphs need room, up to the device's limit, where it used to overwrite its last layer past some 1800 glyphs.
  `FontAtlas.LayerCount` and `LayerCapacity`; `AtlasLayerCount` is gone, `InitialLayerCount` takes its place.
- Lines are as tall as the font says: ascent plus descent plus line gap (`IFont.LineAscent`, `LineDescent`, `LineGap`),
  the baseline half the gap and the ascent below the line's top, taken as HarfBuzz and the browsers take them (the
  typographic metrics when the font sets USE_TYPO_METRICS, the horizontal header's otherwise). Before, the baseline
  stood almost at the bottom of the line and descenders hung below it, so a background or a selection stopped at the
  baseline; and a font with no line gap was laid out with a gap as tall as the font - Source Sans and Cascadia Code at
  more than twice their size. Segoe UI's lines are now 1.33 of its size apart, as on Windows, instead of 1.13.
- `TextLayout` with `TextWrapping.WrapByWords` wraps where Unicode allows a line to end, not only at spaces: after a
  hyphen, between ideographs, never before a closing parenthesis or a comma, never at a no-break space.
- `TextLayout` shapes its text with `TextShaper`: the font's ligatures, contextual alternates, mark positioning and
  kerning apply, and a character the font lacks draws its `.notdef` box instead of nothing. A glyph is no longer one
  character: `GetTextData` lists glyphs with the UTF-16 offset of their first character, and caret positions come from
  `GetCaretStops`.
- `IFont.FeatureCatalog` replaces `FeatureService`: the features a font offers per script (`FontScript`) and language
  system (`FontLanguage`), each with its table and parameters, as the font lists them. Before, the features of every
  script were merged by name and the first script's won, so a Serbian or Bulgarian `locl` was lost behind the Latin one.
  The catalog is read-only: which features apply belongs to the text being shaped (`ShapingOptions`), not to the font.
- Memory blocks in a small heap are a 64th of it, at least 4 MB, instead of an 8th: a process takes about half as much of
  the shared window as before (a designer preview 40 MB instead of 80).
- Running out of memory in every type a buffer allows reports the size, the memory type and the heap.
- Every new memory block is logged at the Debug level with its type, heap and the heap's total in blocks.

### Removed

- `IFont.Baseline`: a made-up metric (`UnitsPerEm - Ascender + LineGap + CapsHeight`); the baseline of a line is
  `LineGap / 2 + LineAscent` below its top.
- The feature application `TextShaper` replaced: `GlyphLayoutContainer`, `IGlyphSubstitutions`, `IGlyphPositioning`,
  `GlyphLayoutData`, `GlyphPosition`, `Glyph.Layout`, `IFont.NotDefLayoutData`, `Feature.Apply`, `Feature.IsEnabled`,
  `FeatureService.EnableFeature` / `ApplyFeature`, and the `SubstituteGlyphs` / `PositionGlyph` methods of lookup
  subtables. They applied one feature at a time with the enabled state kept in the font, and had no implementation for
  contextual subtables.
