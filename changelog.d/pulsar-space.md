### Added

- Pulsar places voices in space: `VoiceSettings.Spatial`, moved by `AudioEngine.SetPlace` and heard by the
  `AudioEngine.SetListener` listener. A voice in space is quieter with its distance by its `Attenuation` (inverse,
  linear or exponential, between near and far), by its `SoundCone` where it faces away, panned by where it is across,
  and higher or lower by the Doppler shift of its velocity and the listener's (`AudioEngineSettings.SpeedOfSound`,
  `SpatialSettings.DopplerScale`). It is mixed to one channel; its own pan is not heard. Its place is relative to the
  listener, in metres: the subtraction is the caller's, in double. Far voices become the unheard ones first.
- `Adamantium.Pulsar` depends on `Adamantium.Mathematics`.
