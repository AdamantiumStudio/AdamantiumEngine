### Changed

- `PhysicsSettings.FindProblem()` answers a `PhysicsSettingsProblem` - `None`, or which setting no simulation takes -
  instead of an English sentence; `Explained()` gives the sentence, for a log or an exception.
  `PhysicsSettings.MostContactPointsPerPair` is the most contact points a pair keeps, for an application to say so.
- A physics file that is not read - a project's physics, a physics material - throws `PhysicsFileException`, an
  `IOException`, whose `Problem` says why as a value: not a file of its kind (damaged XML, or another file's root), a
  value in it that cannot be read, of a later version (`Version`, `ReadsUpTo`), settings refused (`Settings`), a time
  to sleep not finite, a material amount refused. An application says it in its own language; the message stays
  English, for a log. It was an `InvalidDataException` with an English message only, and a value that could not be
  read was told as a file of another kind. Writing physics a read would refuse throws `InvalidOperationException`
  with that `PhysicsFileException` inside.

### Fixed

- A project's physics with sleeping off and a time to sleep below zero is refused, as it is with sleeping on: the time
  is kept to come back when sleeping is turned on, and was refused only then.
