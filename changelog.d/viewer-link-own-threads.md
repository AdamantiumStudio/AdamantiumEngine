### Changed

- A universe host and its viewer talk over a link of their own instead of StreamJsonRpc: MessagePack messages, one
  pipe each way, read and written on threads of the link's own - never the thread pool's. A host a debugger holds,
  its pool held with it, still hears its viewer and answers on the threads the debugger lets run
  (`UniverseHostThreads.ViewerLink`). The messages' writers are made as the first link starts, not first at a stop. A
  call that fails in the host throws `UniverseHostException` with its reason; one called off by its caller is canceled
  at once, the host told.

### Added

- `UniverseLaunch.IsDebugged`: a thread of the host's own (`UniverseHostThreads.Drawing`), made before any of the
  game's code runs, draws the world where a debugger holds the game: `IUniverseHostService.DrawWhereItStands` at a
  stop, then a size given or a surface asked for, until `GoOnAsync` - asked before the game is let go on, done once the
  drawing is over. A new size or ring of the frames' sharing is handed to the viewer with a second drawing
  (`ISharedSurfaceProducer.HandsOverWithNextFrame`).
- `Universe.DrawWhereItStands`: every output drawn once, the world as it stands - the outputs' size taken first, no
  step of the game, no action posted, no end of a frame; never beside the loop's own drawing or its GPU calls, its
  drawing lock taken only if free. Held in the game's first frame, before any service draws, the service of the output
  that came with it is put in first (`EntityServiceManager.TakeInRenderingServices`).
