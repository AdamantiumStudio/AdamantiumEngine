### Added

- Pulsar reads Opus in Ogg (`.opus`, and `.ogg`/`.oga` holding it): every mode of RFC 6716 - SILK, CELT and hybrid,
  frames of 2.5 to 60 ms, one or two channels, the mode switches with their redundant frames - with RFC 8251's updates,
  at 48 kHz. Mapping family 0, and 1 and 255 of up to eight channels, in their speakers' order; the header's output
  gain; pre-skip and the last page's end trimming; a seek to the sample, decoding 80 ms before it. All twelve of the
  specification's test vectors pass, the range decoder's final state of every packet as the reference's.

### Changed

- An Ogg file whose page header is damaged no longer loses the page after it: the next page is looked for from just
  after the damaged one began, and taken where its checksum is its own.
