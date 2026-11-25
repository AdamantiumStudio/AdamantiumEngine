### Added

- A project's physics file (`.aphysics`) holds the solver's settings: `Substeps`, `MaxContactPointsPerPair`,
  `SpeculativeDistance`, `ContactHertz`, `ContactDampingRatio`, `MaxPushOutSpeed`, `RestitutionThreshold`,
  `BodiesSleep`, `SleepSpeed`, `TimeToSleep`, `MaxLinearSpeed` and `MaxStepsPerAdvance`. A file that leaves one out
  takes the solver's default, so files written before read as they did.
- `ProjectPhysics.ToSettings`: the settings a game's simulation is made with, gravity included.
- `PhysicsSettings.MaxStepsPerAdvance` (8 as before, when it was fixed) and `PhysicsSettings.FindProblem`.

### Changed

- With a project's physics, a game takes all of the project's solver settings, not only its steps a second.
- Settings are checked in one place, `PhysicsSettings.FindProblem`, which both the simulation and the project's file
  use. The file also refuses an infinite `TimeToSleep`: bodies that never sleep are `BodiesSleep="false"`.
- `StepClock` takes its limit of steps per advance from the settings; it has no default of its own.
