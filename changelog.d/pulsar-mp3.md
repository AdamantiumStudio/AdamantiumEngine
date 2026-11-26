### Added

- Pulsar reads MP3 files: MPEG-1, 2 and 2.5 Layer III, with middle/side and intensity stereo, short and mixed blocks,
  after any ID3v2 tags or zeros. A LAME tag's delay and padding are left out, so a sound loops without a gap; a seek
  is to the sample. A stream whose frames change from one channel to two is of two. Free-format streams (no bitrate in
  their headers) are not read. Content builds import `.mp3` files as they do WAV and FLAC.
