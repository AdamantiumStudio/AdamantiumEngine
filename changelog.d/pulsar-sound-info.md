### Added

- Pulsar: `SoundInfo.Read` says what a sound file is — codec, format, duration, average bitrate, tags — of its
  beginning only, without its samples: for a player's list of files.

### Fixed

- Pulsar: a sound file on the disk (`SoundFileStorage`) was opened anew for every read, so an MP3, whose frame headers
  are read one by one, took 0.6 s to open for five minutes of sound; small reads now come of 64 KB read ahead (12.7 ms).
