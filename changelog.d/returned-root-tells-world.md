### Fixed

- `EntityManager.ReturnEntity` did not tell the world of a root put back: the root, out of the world, had no world to
  tell, so a universe drawing on demand might not wake for it and the editor might miss it (a delete taken back left the
  scene marked changed now and then). It tells the world itself.
- A root hung under another entity and then taken out kept its owner, which held it in the world: put back with
  `ReturnEntity` it never came back (a delete taken back in the editor lost it). Taken out, it lets go of its owner, as a
  branch does.
- Taking out a root hung under another forgot the camera projection of the other's root: it forgets its own.
