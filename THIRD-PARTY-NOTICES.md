# Third-party notices

Parts of this engine are derived from other people's work. All of it is under the MIT licence, which permits use,
modification and distribution — including inside a closed product — on one condition: the copyright notice and the
licence text travel with the code. The per-file headers in the sources are that notice and must not be removed; this
file carries the licence texts they refer to.

Listed here is code that was *taken in and edited*, not packages consumed as dependencies — those carry their own
licences with them.

**On modifications.** All of this has been changed, some of it heavily. That is what the licence allows, and it does not
transfer anything: the original notice stays with a derived file for as long as any of the original remains in it, and
the changes themselves belong to this project. Both statements are meant to be read together — the header says whose
work it started as, this file says it did not stay that way.

---

## FJCore — JPEG codec

**Where:** `Adamantium.Imaging/Jpeg/` (22 files: decoder, encoder, DCT, colour models, filters)

A pure C# JPEG codec, originally a Fluxcapacity Open Source project by Jeffrey Powers, later mirrored on GitHub after
Google Code shut down. Upstream is no longer maintained — the last code change was in 2016 — which changes nothing about
the grant: an MIT licence, once given, is not withdrawn by a project going quiet.

Our copy diverged from upstream long ago and keeps diverging: it was reworked to fit this engine's imaging types, and
most recently the inverse DCT was rebuilt for speed and a forced `GC.Collect()` taken out of the decode path. Those
changes are this project's; the notice below covers what they were made to.

```
Copyright (c) 2008 Jeffrey Powers for Fluxcapacity Open Source.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

Later maintainers of the same codebase state their copyright as
`(c) 2008-2009 Occipital Open Source, (c) 2010-2013 Brian Donahue, (c) 2012-2013 Anders Gustafsson, Cureos AB`,
under the same licence.

**What it supports**, so nobody expects more from it than it has: baseline (SOF0) and progressive (SOF2) JPEG. Not
JPEG 2000 — that is a different format built on wavelets, and there is no code for it here.

---

## HarfBuzz — Arabic shaper, modified combining classes, vertical forms

**Where:** `Adamantium.Fonts/Shaping/ArabicShaper.cs`, `GetModifiedCombiningClass` in
`Adamantium.Fonts/Shaping/UnicodeData.cs`, and `Adamantium.Fonts/Shaping/VerticalForms.cs`

The joining state table, the list of modifier combining marks and the order the Arabic features apply in follow
HarfBuzz's Arabic shaper (`src/hb-ot-shaper-arabic.cc`, https://github.com/harfbuzz/harfbuzz); the combining classes
remapped for Hebrew, Arabic, Thai and Tibetan follow its `hb-unicode.hh`; the vertical presentation forms text set
top to bottom falls back to without a 'vert' feature follow its `hb_vert_char_for` (`src/hb-ot-shape.cc`). All are
ported to C#. HarfBuzz is under the
"Old MIT" licence:

```
Copyright © 2010-2022  Google, Inc.
Copyright © 2015-2020  Ebrahim Byagowi
Copyright © 2019,2020  Facebook, Inc.
Copyright © 2012,2015  Mozilla Foundation
Copyright © 2011  Codethink Limited
Copyright © 2008,2010  Nokia Corporation and/or its subsidiary(-ies)
Copyright © 2009  Keith Stribley
Copyright © 2011  Martin Hosken and SIL International
Copyright © 2007  Chris Wilson
Copyright © 2005,2006,2020,2021,2022,2023  Behdad Esfahbod
Copyright © 2004,2007,2008,2009,2010,2013,2021,2022,2023  Red Hat, Inc.
Copyright © 1998-2005  David Turner and Werner Lemberg
Copyright © 2016  Igalia S.L.
Copyright © 2022  Matthias Clasen
Copyright © 2018,2021  Khaled Hosny
Copyright © 2018,2019,2020  Adobe, Inc
Copyright © 2013-2015  Alexei Podtelezhnikov

Permission is hereby granted, without written agreement and without
license or royalty fees, to use, copy, modify, and distribute this
software and its documentation for any purpose, provided that the
above copyright notice and the following two paragraphs appear in
all copies of this software.

IN NO EVENT SHALL THE COPYRIGHT HOLDER BE LIABLE TO ANY PARTY FOR
DIRECT, INDIRECT, SPECIAL, INCIDENTAL, OR CONSEQUENTIAL DAMAGES
ARISING OUT OF THE USE OF THIS SOFTWARE AND ITS DOCUMENTATION, EVEN
IF THE COPYRIGHT HOLDER HAS BEEN ADVISED OF THE POSSIBILITY OF SUCH
DAMAGE.

THE COPYRIGHT HOLDER SPECIFICALLY DISCLAIMS ANY WARRANTIES, INCLUDING,
BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
FITNESS FOR A PARTICULAR PURPOSE.  THE SOFTWARE PROVIDED HEREUNDER IS
ON AN "AS IS" BASIS, AND THE COPYRIGHT HOLDER HAS NO OBLIGATION TO
PROVIDE MAINTENANCE, SUPPORT, UPDATES, ENHANCEMENTS, OR MODIFICATIONS.
```

---

## Unicode Character Database

**Where:** `Adamantium.Fonts/Data/` — `Scripts.ucd`, `CombiningClass.ucd`, `Decompositions.ucd`,
`ExtendedPictographic.ucd`, `EmojiPresentation.ucd`, `GraphemeBreak.ucd`, `WordBreak.ucd`, `IndicConjunctBreak.ucd`,
`LineBreak.ucd`, `EastAsianWidth.ucd`, `BidiClass.ucd`, `BidiBrackets.ucd`, `BidiMirroring.ucd`, `JoiningType.ucd`
and `VerticalOrientation.ucd`, embedded in `Adamantium.Fonts`;
and, for the tests only, `GraphemeBreakTest.txt`, `WordBreakTest.txt`, `LineBreakTest.txt`, `BidiTest.txt` and
`BidiCharacterTest.txt` in `Tests/Adamantium.FontTests/Unicode/`.

Data, not code: the script, canonical combining class, canonical decomposition, Extended_Pictographic,
Emoji_Presentation, grapheme, word and line break, Indic_Conjunct_Break, East_Asian_Width, Bidi_Class,
Bidi_Paired_Bracket, Bidi_Mirroring_Glyph, Joining_Type, Joining_Group and Vertical_Orientation properties of
Unicode 16.0, cut down from the UCD files
(`Scripts.txt`, `DerivedCombiningClass.txt`, `UnicodeData.txt`, `DerivedNormalizationProps.txt`, `emoji-data.txt`,
`GraphemeBreakProperty.txt`, `WordBreakProperty.txt`, `DerivedCoreProperties.txt`, `LineBreak.txt`,
`EastAsianWidth.txt`, `DerivedBidiClass.txt`, `BidiBrackets.txt`, `BidiMirroring.txt`, `ArabicShaping.txt`,
`VerticalOrientation.txt`) to the
columns the text shaper, the text boundaries and the bidirectional algorithm read, with adjacent ranges joined.

```
UNICODE LICENSE V3

COPYRIGHT AND PERMISSION NOTICE

Copyright © 1991-2026 Unicode, Inc.

NOTICE TO USER: Carefully read the following legal agreement. BY
DOWNLOADING, INSTALLING, COPYING OR OTHERWISE USING DATA FILES, AND/OR
SOFTWARE, YOU UNEQUIVOCALLY ACCEPT, AND AGREE TO BE BOUND BY, ALL OF THE
TERMS AND CONDITIONS OF THIS AGREEMENT. IF YOU DO NOT AGREE, DO NOT
DOWNLOAD, INSTALL, COPY, DISTRIBUTE OR USE THE DATA FILES OR SOFTWARE.

Permission is hereby granted, free of charge, to any person obtaining a
copy of data files and any associated documentation (the "Data Files") or
software and any associated documentation (the "Software") to deal in the
Data Files or Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, and/or sell
copies of the Data Files or Software, and to permit persons to whom the
Data Files or Software are furnished to do so, provided that either (a)
this copyright and permission notice appear with all copies of the Data
Files or Software, or (b) this copyright and permission notice appear in
associated Documentation.

THE DATA FILES AND SOFTWARE ARE PROVIDED "AS IS", WITHOUT WARRANTY OF ANY
KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT OF
THIRD PARTY RIGHTS.

IN NO EVENT SHALL THE COPYRIGHT HOLDER OR HOLDERS INCLUDED IN THIS NOTICE
BE LIABLE FOR ANY CLAIM, OR ANY SPECIAL INDIRECT OR CONSEQUENTIAL DAMAGES,
OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS,
WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION,
ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THE DATA
FILES OR SOFTWARE.

Except as contained in this notice, the name of a copyright holder shall
not be used in advertising or otherwise to promote the sale, use or other
dealings in these Data Files or Software without prior written
authorization of the copyright holder.
```

---

## ISO 639-3 code tables

**Where:** `Adamantium.Fonts/Data/Iso639.dat` — the two-letter ISO 639-1 codes with their three-letter ISO 639-3
codes, unchanged, taken from the code tables SIL International publishes as the ISO 639-3 Registration Authority
(https://iso639-3.sil.org).

---

## Hyphenation patterns (hyph-utf8)

**Where:** `Adamantium.Fonts/Data/Hyphenation/` — `hyph-ru.tex`, `hyph-en-us.tex`, `hyph-en-gb.tex`,
`hyph-de-1996.tex`, `hyph-fr.tex` and `hyph-es.tex`, embedded in `Adamantium.Fonts`, unchanged, from the hyph-utf8
package of the TeX hyphenation project (https://github.com/hyphenation/tex-hyphen, commit
`5684c0f51c0b81133db2efbe60a408b4155a3ff5`).

Data, not code: TeX's hyphenation patterns and exception lists, read by `Hyphenator`. Each file keeps its own header,
which names its authors and its licence and must stay with it:

| File | Copyright | Licence |
|------|-----------|---------|
| `hyph-ru.tex` | © 1999-2003 Alexander I. Lebedev | LaTeX Project Public License 1.2 or later (https://latex-project.org/lppl/) |
| `hyph-en-us.tex` | © 1990, 2004, 2005 Gerard D.C. Kuiken | Copying and distribution, with or without modification, permitted provided the copyright notice and this notice are preserved |
| `hyph-en-gb.tex` | © 1992, 1996, 2005, 2016 Dominik Wujastyk, Graham Toal | MIT |
| `hyph-de-1996.tex` | © 2013-2024 Stephan Hennig, Werner Lemberg, Günter Milde, Sander van Geloven, Georg Pfeiffer, Gisbert W. Selke, Tobias Wendorf, Keno Wehr | MIT |
| `hyph-fr.tex` | © 1994-2002 Daniel Flipo, Bernard Gaulle, 2016 Arthur Reutenauer | MIT |
| `hyph-es.tex` | © 1993, 1997 Javier Bezos, 2001-2019 Javier Bezos, CervanTeX | MIT |

The Russian patterns are under the LPPL, not the MIT licence: they are distributed unmodified, as the LPPL allows, and
a change to them would have to go into a file of another name. The British patterns were made from a word list of
Oxford University Press that is not part of them and is not distributed.

---

## Google color font test glyphs

**Where:** `Tests/Adamantium.FontTests/ColorFonts/` — `test_glyphs-glyf_colr_1.ttf`,
`test_glyphs-glyf_colr_1_variable.ttf`, `samples-sbix.ttf`, `samples-picosvg.ttf`, `samples-untouchedsvg.ttf` and
`samples-glyf_colr_1.ttf`, unchanged, from https://github.com/googlefonts/color-fonts,
used only by the tests. Copyright Google LLC, under the Apache License 2.0, whose text is `LICENSE.txt` beside them.

---

## ChromaCheck test fonts

**Where:** `Tests/Adamantium.FontTests/ColorFonts/` — `chromacheck-cbdt.ttf`, `chromacheck-sbix.ttf` and
`chromacheck-colr.ttf`, unchanged, from
https://github.com/RoelN/ChromaCheck (as HarfBuzz's tests carry them), used only by the tests. Copyright Roel Nieskens
and Google LLC, under the MIT License, whose text is `LICENSE-chromacheck.txt` beside them.

---

## Noto Color Emoji subsets

**Where:** `Tests/Adamantium.FontTests/ColorFonts/` — `NotoColorEmoji.subset.ttf`,
`NotoColorEmoji.subset.multiple_size_tables.ttf` and `NotoColorEmoji.subset.index_format3.ttf`, unchanged, the subsets
of Noto Color Emoji HarfBuzz's tests carry (https://github.com/harfbuzz/harfbuzz/tree/main/test/api/fonts), used only by
the tests. Copyright Google LLC, under the SIL Open Font License 1.1, whose text is `LICENSE-NotoColorEmoji.txt` beside
them. `NotoColorEmoji.subset.index_format2_image_format19.ttf`, `…index_format4_image_format18.ttf` and
`…index_format5_image_format19.ttf` beside them are modified versions of the first, made for the tests: the same images
stored in the other formats of the 'CBLC' and 'CBDT' tables.

---

## Roboto Flex

**Where:** `Tests/Adamantium.FontTests/Variations/RobotoFlex-Variable.ttf`, unchanged, the variable font from
https://github.com/google/fonts/tree/main/ofl/robotoflex, used only by the tests. Copyright 2017 The Roboto Flex
Project Authors, under the SIL Open Font License 1.1, whose text is `RobotoFlex-OFL.txt` beside it.

`RobotoFlex-NoHVAR.subset.ttf` beside it is a modified version of that font, made for the tests: four letters of it,
without its `HVAR` and layout tables, and with the phantom-point variations of one composite glyph moved.
`RobotoFlex-NoGvar.subset.ttf` is another: three letters of it, without its `gvar`, `MVAR` and layout tables.

---

## Noto Sans Hebrew

**Where:** `Tests/Adamantium.FontTests/ScriptFonts/NotoSansHebrew-Regular.ttf`, unchanged, from
https://github.com/notofonts/hebrew, used only by the tests. Copyright 2022 The Noto Project Authors, under the SIL
Open Font License 1.1, whose text is `LICENSE-NotoSansHebrew.txt` beside it.

---

## Noto Sans Arabic, Noto Naskh Arabic

**Where:** `Tests/Adamantium.FontTests/ScriptFonts/NotoSansArabic-Regular.ttf` and `NotoNaskhArabic-Regular.ttf`,
unchanged, from https://github.com/notofonts/arabic, used only by the tests. Copyright 2022 The Noto Project Authors,
under the SIL Open Font License 1.1, whose text is `LICENSE-NotoArabic.txt` beside them.

---

## Noto Nastaliq Urdu

**Where:** `Tests/Adamantium.FontTests/ScriptFonts/NotoNastaliqUrdu-Regular.ttf`, unchanged, from
https://github.com/notofonts/nastaliq, used only by the tests. Copyright 2022 The Noto Project Authors, under the SIL
Open Font License 1.1, whose text is `LICENSE-NotoNastaliqUrdu.txt` beside it.

---

## Noto Sans Syriac, Noto Sans Mongolian, Noto Sans NKo, Noto Sans Adlam

**Where:** `Tests/Adamantium.FontTests/ScriptFonts/` — `NotoSansSyriac-Regular.ttf`, `NotoSansMongolian-Regular.ttf`,
`NotoSansNKo-Regular.ttf` and `NotoSansAdlam-Regular.ttf`, unchanged, the hinted builds from
https://github.com/notofonts/notofonts.github.io, used only by the tests. Copyright 2022 and 2023 The Noto Project
Authors, under the SIL Open Font License 1.1, whose text is `LICENSE-NotoSansSyriac.txt`, `LICENSE-NotoSansMongolian.txt`,
`LICENSE-NotoSansNKo.txt` and `LICENSE-NotoSansAdlam.txt` beside them.

---

## Ponomar Unicode, Monomakh Unicode

**Where:** `Tests/Adamantium.FontTests/ScriptFonts/PonomarUnicode.otf` and `MonomakhUnicode.otf`, unchanged, from the
fonts-churchslavonic release 2.2.1 of the Slavonic Computing Initiative (https://github.com/typiconman/fonts-cu), used
only by the tests. Copyright 2013-2020 (Ponomar) and 2006-2020 (Monomakh) Aleksandr Andreev and Nikita Simmons, under
the SIL Open Font License 1.1, whose text is `LICENSE-ChurchSlavonic.txt` beside them.

---

## Adamantium Jalt Test

**Where:** `Tests/Adamantium.FontTests/TTFFonts/JaltByScript-Regular.ttf`, made for the tests from Source Sans 3
Regular beside it: its Latin and Cyrillic lowercase only, without its layout tables, with a 'jalt' feature of its own
for the Cyrillic script, renamed as the license asks of a modified version. Copyright 2010-2020 Adobe, with Reserved Font
Name 'Source', under the SIL Open Font License 1.1, whose text is `source-sans-3v028R/LICENSE.md`.

---

## SharpDX

**Where:** ten files still carry its notice, and only those ten are derived from it —

- `Adamantium.Imaging`: `ImageDescription.cs`, `PixelBuffer.cs`, `PixelBufferArray.cs`, and DDS handling
  (`DdsHelper.cs`, `DdsFlags.cs`, `FourCC.cs`, `HeaderDXT10.cs`)
- `Adamantium.Core`: `DataBuffer.cs`, `NamedObject.cs`
- `Adamantium.Mathematics`: `ViewportF.cs`

Everything else that once came from it has been rewritten and carries no notice.

**Its own ancestry.** SharpDX did not write its maths either: that part came in from SlimMath, itself a port of the
maths in SlimDX, under MIT/X11. So `ViewportF.cs` — and anything else here descended from that maths — traces back
further than the header says. The terms are the same all the way down, which is why one notice covers the chain; it is
recorded because provenance is worth knowing accurately, not because a second obligation exists.

Maths is also where this project keeps rewriting. As a file stops containing any of the original, its notice stops
applying and can go — but that is judged per file, on what is actually left in it, not on how much work has been done
around it.

```
Copyright (c) 2010-2014 SharpDX - Alexandre Mutel

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
