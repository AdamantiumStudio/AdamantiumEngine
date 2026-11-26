### Added

- `Material.Lighting`: how a surface is lit, `LightingModel.Simple` (the Blinn-Phong model, as before, and the default)
  or `LightingModel.Physical` - metalness and roughness by the Cook-Torrance model, its color, metalness, roughness and
  emission worked in linear light. A simple scene keeps the cheaper model; each material chooses.
- `Material.MetallicRoughnessTexture`, `NormalTexture`, `OcclusionTexture` and `EmissiveTexture`: the maps of a
  physical material, loaded from their paths on import. A normal map needs no tangents in the mesh.
- `Material.Emission` is drawn by both models: a surface gives off its light whatever lights it, an emissive map
  shaping it.
- An imported material that knows its metalness, or carries the map of it, is lit physically.
- `ForwardPlusRenderingProcessor.HighDynamicRange`: the scene lit in linear light without a ceiling, into a half-float
  frame of its own, then brought to the screen by the AgX tone curve - bright colors go to white as on film - with
  `Exposure` in stops. Its brightest light spreads into a glow, `Glow` its share: the frame copied smaller and smaller
  and added back up. Off by default, every surface drawn straight into the frame as before.
