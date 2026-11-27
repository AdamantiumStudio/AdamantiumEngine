### Changed

- Pulsar: an `AudioPlayback` of its own output starts it when made, not at its first play, and keeps 50 ms of sound
  ready rather than 100: its first play is heard as soon as any other (from 115-204 ms to about 40 ms, plus the
  output's buffer).
