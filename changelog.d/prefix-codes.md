### Added

- `Adamantium.Core.Compression.PrefixCode`: one decoder of prefix (Huffman) codes for every format, made of codewords
  (`PrefixCodeWord`) or canonical codes of lengths (`TryCanonical`), of up to 32 bits, read with the next bit the
  highest or the lowest (`PrefixBitOrder`). A look-up of 9 bits, and a table of its own for each further 9.

### Changed

- JPEG, JBIG2, CCITT fax (its run lengths and two-dimensional modes), MP3 and PRC (its Huffman coded arrays and its
  table of doubles) decode their prefix codes by `PrefixCode`, each its own code assignment and bit reading kept.
  JPEG decodes as fast as before; JBIG2 and PRC read a code at a look-up, not a bit at a time.
