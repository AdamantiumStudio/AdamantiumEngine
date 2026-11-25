### Added

- Fonts in new PDF documents: `PdfDocumentWriter.Font` gives a font the document carries, `PdfContentBuilder.BeginText`,
  `Glyph` and `EndText` draw in it. Only the glyphs drawn are carried, with their widths and their text, so it is
  selected, found and copied; the font is written once, when the document is finished.
- `CffSubset`: some glyphs of any font - TrueType, CFF, a variable one at its axis values - written as a CFF font program
  from their outlines.
- `IFont.Embedding`: what a font's license lets a document carry of it ('OS/2' fsType).

### Fixed

- A TrueType contour with no point on it starts midway between its last control point and its first, not on the last.
