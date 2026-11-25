### Added

- `Adamantium.Pulsar`, the audio engine, begun: `AudioOutput.CreateDefault` plays what an `IAudioRenderer` makes to the
  default device of Windows (WASAPI, shared mode), and `AudioOutput.CreateSilent` runs by the clock with no device. An
  output counts the gaps heard, by the device's own clock; its thread allocates nothing.
- `Adamantium.Win32.Audio`: WASAPI called through its method tables.
