### Fixed

- Pulsar: an `AudioPlayback`'s voice at volume 0, mixed only for its spectrum, took the place of a heard voice of a
  lower `Priority` among the `AudibleVoices`: it ranks below every heard voice now, whatever its priority, and
  `AudioEngine.AudibleVoices` counts only voices heard.
