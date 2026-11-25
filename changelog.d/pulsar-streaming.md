### Added

- FLAC files, decoded by Pulsar itself, and loops written in their comments (`LOOPSTART`, `LOOPLENGTH` or `LOOPEND`).
- `StreamedSound`: a sound read and decoded while it plays, on a thread of the engine's. `AudioEngine` is disposable.
- Content builds import WAV and FLAC files into asset files (.aemf); a sound longer than ten seconds is streamed, unless
  the `Streaming` setting says otherwise. A game loads one as a `Sound`.
- `AssetFile.OpenSection`: a section of an asset file, read in parts.

### Changed

- `WavFile` is `SoundFile`, which reads FLAC too. `Sound` is what an engine plays: a `SoundData` or a `StreamedSound`.
- An asset file made from a sound is of `AssetKind.Sound`, no longer a model. A WAV or FLAC file that can not be read -
  one of more than two channels, say - fails the content build, where it was copied before.
