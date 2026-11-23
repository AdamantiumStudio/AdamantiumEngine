# Changelog entries waiting for a release

A change does not write into `CHANGELOG.md`: it adds its entry here, in a file of its own. Changes made side by side
then never touch the same lines, and never conflict over the changelog.

## Writing an entry

- One file a change, named after its branch: `changelog.d/<branch>.md`.
- Under a `### Section` heading of [Keep a Changelog](https://keepachangelog.com/en/1.1.0/): `Added`, `Changed`,
  `Deprecated`, `Removed`, `Fixed` or `Security`. A change that does more than one kind of thing has more than one
  heading in its file.
- Each entry a list item, written as it will stand in `CHANGELOG.md`, lines wrapped at 120 characters:

```markdown
### Fixed

- `EntityComponentCollection.CheckItems` did not call the base check, so one a parent gains would be passed by: it
  calls `base.CheckItems` first.
```

A change made later to the same entry edits its file here while it waits.

## At a release

`collect-changelog.ps1`, at the root, puts every entry under the same section of `## Unreleased` in `CHANGELOG.md` and
deletes the files; `-WhatIf` shows the result and deletes nothing. The version is named after that, as before.
