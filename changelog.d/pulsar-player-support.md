### Added

- Pulsar: `AudioPlayback`, one sound file played as a player plays it — `Open`, `Play`, `Pause`, `Stop`, `Seek`,
  `Volume`; `State`, `Position`, `Duration`; `Info` (`SoundInfo`: codec, format, duration, average bitrate, tags);
  `ReadSpectrum` (bands from 40 Hz to 16 kHz of what is heard); `ReadWaveform` (`SoundWaveform`, read in the
  background); `StateChanged` and `Ended` on the thread it was made on. It keeps its own engine and the default device,
  or plays to an output given, or through the application's `AudioEngine`.
- Pulsar: `Sound.Codec` and `ISoundDecoder.Codec` name a sound's format (`WAV`, `FLAC`, `MP3`, `Vorbis`, `Opus`);
  `Sound.Tags` and `ISoundDecoder.Tags` are its title, artist and album, read of a WAV's `LIST INFO`, Vorbis comments
  of FLAC, Vorbis and Opus, and an MP3's ID3v2.2–2.4 tag, or ID3v1 where it has no other. A decoder of one's own need
  not say them: they are null and none by default.
