### Added

- `PhysicsMaterialFile.Write` and `ProjectPhysics.Write` (to a path or a stream), `PhysicsMaterialFile.Read(string)` and
  `PhysicsMaterialFile.From` (the file of a material): the editor can make and change `.aphysmat` and `.aphysics` files.
  A file is always written at the version this engine writes, and one its `Read` would refuse is not written
  (`InvalidOperationException`).
- `AtomicFile.Write` (`Adamantium.Core`): writes a file whole or not at all, through a hidden file beside it moved over
  the old one.

### Changed

- `SceneFile` and the content builder write their files through `AtomicFile` instead of their own copies of it.
