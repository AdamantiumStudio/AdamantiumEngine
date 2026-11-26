### Added

- Pulsar: what a player shows of a sound. `Sound.Codec` and `ISoundDecoder.Codec` name its format (`WAV`, `FLAC`,
  `MP3`, `Vorbis`, `Opus`); `Sound.Tags` and `ISoundDecoder.Tags` are its title, artist and album, read of a WAV's
  `LIST INFO`, Vorbis comments of FLAC, Vorbis and Opus, and an MP3's ID3v2.2–2.4 tag, or ID3v1 where it has no other.
  A decoder of one's own need not say them: they are null and none by default.
- Pulsar: `AudioEngine.CopyPlayed` gives the last frames mixed, up to `AudioEngine.PlayedCapacity`, for a spectrum or
  a level meter to show.
- Pulsar: `SoundPeaks`, a sound's waveform for drawing — the least and the greatest sample of each part of it, of each
  channel, read a part at a time on a thread of its own.
