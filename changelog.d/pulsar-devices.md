### Added

- Pulsar plays on Linux, through PulseAudio or PipeWire in its place, and on macOS, through an AudioQueue. Where there
  is no device the output runs by the clock, unheard (`AudioOutputState.Silent`, `IAudioOutput.DeviceProblem`), and is
  heard once there is one; on Windows it moves to another default device.
- `Adamantium.MacOS.AudioToolbox`: output audio queues. `Adamantium.Win32.Audio.DefaultOutputWatch`.

### Removed

- `AudioException`: an output with no device starts all the same.
