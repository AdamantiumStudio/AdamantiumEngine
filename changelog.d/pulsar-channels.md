### Added

- Pulsar plays sounds of up to eight channels, WAV and FLAC alike, mixed down to two by ITU-R BS.775: the center and
  the surrounds 3 dB down in their sides, the low frequencies left out; not scaled down, so a loud 5.1 sound goes past
  1, as voices played together do. `AudioFormat.Speakers` says where each channel is played, from a WAV file's channel
  mask or a FLAC file's `WAVEFORMATEXTENSIBLE_CHANNEL_MASK`; where a file says none, they are the usual of their number.
  Two channels other than the front two are mixed down by them too; one is panned.

### Changed

- `AudioFormat` is no positional record: it is made by its constructor, which takes the speakers too, and has no
  `Deconstruct`. A WAV or FLAC file of three to eight channels is read, not refused.
