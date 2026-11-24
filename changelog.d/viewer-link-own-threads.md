### Changed

- A universe host and its viewer talk over a link of their own instead of StreamJsonRpc: MessagePack messages, one
  pipe each way, read and written on threads of the link's own - never the thread pool's. A host a debugger holds,
  its pool held with it, still hears its viewer and answers on the threads the debugger lets run
  (`UniverseHostThreads.ViewerLink`). The messages' writers are made as the first link starts, not first at a stop. A
  call that fails in the host throws `UniverseHostException` with its reason - so does one whose answer cannot be read;
  one called off by its caller is canceled at once, the host told.

### Added

- `UniverseLaunch.IsDebugged`: a thread of the host's own (`UniverseHostThreads.Drawing`), made before any of the
  game's code runs, draws the world where a debugger holds the game: `IUniverseHostService.DrawWhereItStands` at a
  stop, then a size given or a surface asked for, until `GoOnAsync` - asked before the game is let go on, done once the
  drawing is over, answered from the drawing's thread: the link reads on meanwhile, a call off or a Stop among it. A
  new size or ring of the frames' sharing is handed to the viewer with a second drawing
  (`ISharedSurfaceProducer.HandsOverWithNextFrame`).
- `Universe.DrawWhereItStands`: every output drawn once, the world as it stands - the outputs' size taken first, no
  step of the game, no action posted, no end of a frame; never beside the loop's own drawing or its GPU calls, its
  drawing lock taken only if free. Held in the game's first frame, before any service draws, the service of the output
  that came with it is put in first (`EntityServiceManager.TakeInRenderingServices`).
- `IEntityService.UpdateWhereItStands`, called by `Universe.DrawWhereItStands(placesWhatMoved)` before it draws -
  held at a breakpoint or a step (`IUniverseHostService.DrawWhereItStands(heldAtAStatement)`), not paused anywhere:
  what the game changed since the last frame is drawn where it is - no time goes by, nothing steps.
  `TransformService` places what moved, no
  animation stepped; `InputService` has the cameras take a size given and a move made meanwhile. Only the engine's
  services, and only while the game is held inside a game service's update - anywhere else the engine may be halfway
  through changing the world; one that fails leaves the others to it. A breakpoint in a loop that moves the planets
  shows each moved, and a view resized at a stop keeps its proportions.
