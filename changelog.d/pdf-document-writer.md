### Added

- `PdfDocumentWriter`: a new PDF document written page by page - each page and what it draws go to the stream as
  they are added, so a long document is never held whole; `Finish` writes the page tree, the catalog, the information
  (`Title`, `Author`, `Subject`, `Creator`, `Producer`, `Created`), a file identifier and the cross-reference table. A
  stream drawn on several pages - one picture - is written once. Read back by our reader and by MuPDF.
- `PdfContentBuilder` draws pictures and forms (`Draw`), shadings (`Shade`), shading patterns (`FillPattern`,
  `StrokePattern`) and soft masks (`SoftMask`, by luminosity or alpha), each named once however often it is drawn;
  `ToFormXObject(bbox, PdfTransparencyGroup)` makes a transparency group, `ToContentStream` and `ToResources` a page.
- `PdfImages.FromRgba`: a picture from straight RGBA pixels, its alpha a soft mask of its own when it has any.
- `PdfShadings`: axial and radial gradients through any number of stops (`PdfGradientStop`, in color or gray for a
  mask), stitched, their end colors carried on; triangles shaded between their corners (`PdfShadedVertex`, type 4);
  `Pattern` to fill or stroke with one.

### Changed

- `PdfIncrementalUpdate.Add` writes the streams inside what it adds - an appearance's pictures and masks - as objects of
  their own; before, an appearance that held one could not be written.
