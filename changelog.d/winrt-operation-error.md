### Changed

- `StringListOperations.ReadAsync` throws a `COMException` with Windows' error code when the operation fails, where it gave
  no strings - as if there were none to give. A canceled one still gives none.

### Fixed

- `StringListOperations.ReadAsync` lets go of the operation at once: its wrapper, counted again as it was handed to the
  handler, stayed until a garbage collection.
