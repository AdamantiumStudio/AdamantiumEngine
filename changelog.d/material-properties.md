### Added

- `PhysicsMaterial.StaticFriction` and `DynamicFriction` in place of `Friction`: a touch is held by its static friction,
  over all its points together as a load shifts between them, until it slips faster than 2 cm/s; from then it slides
  by the dynamic until it stops - a crate rests on a slope its sliding friction would not hold. A dynamic friction above
  the static slides by the static.
- `PhysicsMaterial.RollingResistance`: a body rolling over another is held back by a turn of the resistance times how
  hard it is pressed times the radius it rolls on (a tyre on asphalt about 0.01). Only a moving round part rolls - a
  sphere, a capsule, a cylinder touched on its side, the smaller of two - flat parts tip as before. Met by
  `FrictionCombine`.
- `PhysicsMaterial.Surface`: what surface a material is ("Ice", "Asphalt"), told with contacts
  (`ContactEventArgs.FirstSurface` / `SecondSurface`) and asked of a hit's part with
  `PhysicsSimulation.SurfaceOf(body, part)`; while the simulation's thread steps, the queries' copies follow a body's
  material. Games on entities have it with `EntityContactEventArgs.FirstSurface` / `SecondSurface` and
  `EntityRayHit.Surface` / `EntityShapeHit.Surface`.
- `.aphysmat` version 2: `StaticFriction`, `DynamicFriction`, `RollingResistance` and `Surface` (trimmed, blank for
  none); a version 1 file's `Friction` is read as both frictions, and a file naming no version is of version 1.

### Removed

- `PhysicsMaterial.Friction` and `PhysicsMaterialFile.Friction`: `StaticFriction` and `DynamicFriction`.
