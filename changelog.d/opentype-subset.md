### Added

- `OpenTypeSubset`: the CFF program of `CffSubset` written as an OpenType file - 'head', 'hhea', 'hmtx', 'maxp', 'OS/2',
  'name', 'post' and 'cmap' (formats 4 and 12) beside it - for an XPS document to embed.
- The XPS object model's glyphs, their editor and font resources: `IXpsOMGlyphs`, `IXpsOMGlyphsEditor`,
  `IXpsOMFontResource`, `XpsGlyphIndex`, `XpsFontEmbedding`; `IXpsOMObjectFactory.CreateGlyphs` and
  `CreateFontResource` take and give them.

### Fixed

- `CffSubset.IndicesOf` said it kept the order glyphs were asked for in; it lays them out by their indices, as it did.
