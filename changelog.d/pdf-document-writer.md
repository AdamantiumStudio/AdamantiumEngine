### Added

- `PdfDocumentWriter`: a new PDF document written page by page - each page and what it draws go to the stream as
  they are added, so a long document is never held whole; `Finish` writes the page tree, the catalog, the information
  (`Title`, `Author`, `Subject`, `Creator`, `Producer`, `Created`), a file identifier and the cross-reference table. A
  stream drawn on several pages - one picture - is written once. Read back by our reader and by MuPDF.
- `PdfContentBuilder` draws pictures and forms (`Draw`), shadings (`Shade`), shading patterns (`FillPattern`,
  `StrokePattern`) and soft masks (`SoftMask`, by luminosity or alpha), each named once however often it is drawn;
  `ToFormXObject(bbox, PdfTransparencyGroup)` makes a transparency group, `ToContentStream` and `ToResources` a page.
- `PdfTransparencyGroup`, read from forms before, is made too: its constructor, `Layer` (isolated, DeviceRGB) and
  `GrayMask` (isolated, DeviceGray).
- `PdfImages.FromRgba`: a picture from straight RGBA pixels, its alpha a soft mask of its own when it has any.
- `PdfShadings`: axial and radial gradients through any number of stops (`PdfGradientStop`, in color or gray for a
  mask), stitched, their end colors carried on; triangles shaded between their corners (`PdfShadedVertex`, type 4);
  `Pattern` to fill or stroke with one.

### Changed

- `PdfIncrementalUpdate.Add` and `Replace` write the streams inside what they are given - an appearance's pictures
  and masks - as objects of their own, each once however many objects of the update hold it; a stream of an encrypted
  file copied for that keeps its bytes decrypted once. Before, an object holding a stream could not be written.
- A stream holding itself, directly or through others, is refused with a `PdfException` - not a stack overflow; the
  new document refuses a stream read from another file, whose references are that file's.
