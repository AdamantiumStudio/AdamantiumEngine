### Changed

- A universe host and its viewer talk over a link of their own instead of StreamJsonRpc: MessagePack messages, one
  pipe each way, read and written on threads of the link's own - never the thread pool's. A host a debugger holds,
  its pool held with it, still hears its viewer and answers on the threads the debugger lets run
  (`UniverseHostThreads.ViewerLink`). A call that fails in the host throws `UniverseHostException` with its reason; one
  called off by its caller is canceled at once, the host told.

### Added

- `UniverseLaunch.IsDebugged`: a thread of the host's own (`UniverseHostThreads.Drawing`) then draws the world where a
  debugger holds the game, as the viewer asks - a size given, a surface asked for, or `IUniverseHostService.DrawWhereItStands`
  at a stop. The debugger lets it run only while it holds the game.
- `Universe.DrawWhereItStands`: every output drawn once, the world as it stands - the outputs' size taken first, no
  step of the game, no action posted, no end of a frame.
