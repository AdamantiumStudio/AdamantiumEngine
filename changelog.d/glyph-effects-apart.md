### Added

- `FrozenGlyphRun.EffectsCount`: how many of a run's first items are its glyphs' effects - reflections, shadows, glows
  and outlines - drawn under the glyphs that follow; `FrozenGlyphRun.EffectsOnly()` gives the run with its effects
  alone, sharing its items, for a print that lays them under glyphs it draws as vector.
- `TextLayout.HasGlyphEffects`: whether any glyph wears an outline, a shadow, a glow or a reflection, the layout's own
  or one a stretch of its text sets.
