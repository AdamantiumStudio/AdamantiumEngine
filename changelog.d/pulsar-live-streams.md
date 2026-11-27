### Added

- Pulsar: `AudioPlayback.Open(Uri)` plays an internet radio station - an Icecast or SHOUTcast v2 stream over HTTP of
  MP3, or of Ogg Vorbis or Opus, link after link of its chain. It connects in the background: `Info` says the station
  once it did (`SoundInfo.IsLive`, `SoundInfo.Station`, the bitrate it says), `StreamTitle` and `StreamTitleChanged`
  the song that plays now, from ICY's metadata or an Ogg link's tags. `Play` waits in the new
  `AudioPlaybackState.Connecting` until two seconds came, so it plays without a gap. A drop is connected again after
  1, 2 and 4 s, five times, while what came plays on; a station that will not come back stops once that was played,
  and says why in `AudioPlayback.Problem`, without `Ended`. AAC and lists of addresses are said not to play.
- Pulsar: MP3 and Ogg streams that do not seek are decoded as they come, frame after frame or page after page, with
  no table of frames or index of pages; bytes that are no frame are passed by to the next one.
