### Fixed

- Two PDF timing tests failed on a busy macOS runner: the tint transform's grid is measured against the same samples
  read as plain gray, the quickest of alternating runs of each, not against a clock; the linear-time checks take the
  best of three runs.
