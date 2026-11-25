### Changed

- `StringListOperations.ReadAsync` throws a `COMException` with Windows' error code when the operation fails, where it gave
  no strings - as if there were none to give. A canceled one still gives none.
