### Changed

- Pulsar: `AudioPlayback.ReadSpectrum` shows the sound playing at its own loudness, whatever the `Volume` - as classic
  players do, so a quiet or silent player still shows it - and only that playback's sound, in an engine that plays
  others too. An `AudioPlayback` plays through a bus of its own under the master, whose volume its `Volume` is; given
  the application's `AudioEngine`, it takes one of its buses, and gives it back when disposed.
