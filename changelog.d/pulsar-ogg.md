### Added

- Pulsar reads Ogg files (`.ogg`, `.oga`) of Vorbis or FLAC: Vorbis I with both floors, all three residues and up to
  eight channels in their speakers' order, a seek to the sample; FLAC in Ogg as native FLAC files are read. The first
  stream of a codec it knows is the sound, others beside it passed by, links chained after it not read. A first page
  that says fewer samples than its packets have cuts them from the beginning, the last page from the end. A damaged
  page is silence, the rest read on. `LOOPSTART`/`LOOPLENGTH`/`LOOPEND` comments are the loop, as in FLAC. Opus is
  refused as not read yet. Content builds import `.ogg` and `.oga` files as they do the others.
