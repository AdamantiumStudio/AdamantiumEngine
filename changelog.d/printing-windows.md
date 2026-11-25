### Added

- `Adamantium.Win32.Printing`: what printing on Windows needs.
  - `Winspool`: the spooler's printers (`EnumPrinters`, `GetDefaultPrinter`, `PRINTER_INFO_2` and `PRINTER_STATUS_*`),
    a printer driver's own settings window (`DocumentProperties`), and the PrintTicket provider (`PTOpenProvider`,
    `PTGetPrintCapabilities`, `PTMergeAndValidatePrintTicket`, and conversion between a DEVMODE and a PrintTicket).
  - `PrintDocumentPackage` with `IPrintDocumentPackageTargetFactory` and `IPrintDocumentPackageTarget`: a print job a
    document is written into as XPS, with its PrintTicket - or, given a stream, printed into it.
  - `Printing.Xps`: the XPS object model's factory, pages, canvases, paths, geometries, solid, gradient, image and visual
    brushes, image and PrintTicket resources, and the package writer.
  - `ComStream`: a .NET stream as a COM `IStream`.
