### Added

- Adamantium.Win32: `IFileOpenDialog` declares the rest of its slots - `ClearClientData`, `SetFilter`, `GetResults`,
  `GetSelectedItems` - with `IShellItemArray` and `ShellDialog.AllowMultiSelect`, so an Open dialog can let several
  files be chosen at once.
- Adamantium.MacOS: `NSSavePanel.SetAllowsMany` and `NSSavePanel.RunForMany`, the same for an open panel.
