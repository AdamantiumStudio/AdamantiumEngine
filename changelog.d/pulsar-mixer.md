### Added

- `AudioEngine`: Pulsar plays sounds. Voices with volume, pitch, pan, fades, pauses, loops and a start to the frame,
  mixed through a tree of buses with volume, mute, solo and pause. Of more voices than are heard the most important and
  loudest are; the rest go on unheard and are heard from where they got to.
- `SoundData` and `WavFile`: sounds in memory, read from WAV files.
