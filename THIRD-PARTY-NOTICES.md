# Third-party notices

The project uses the following direct third-party packages:

| Package | Version | License | Purpose |
|---|---:|---|---|
| ClosedXML | 0.105.1 | MIT | Excel .xlsx inspection reports |
| PDFsharp-MigraDoc-WPF | 6.2.4 | MIT | PDF inspection reports and Windows/WPF font support |
| Tesseract | 5.2.0 | Apache-2.0 | OCR engine integration |
| PDFtoImage | 5.4.0 | MIT | PDF page rasterization |
| IxMilia.Dxf | 0.8.4 | MIT | DXF parsing |
| Microsoft.Data.Sqlite | 8.0.33 | MIT | Embedded SQLite project storage |

Package metadata and license declarations are available from NuGet:

- https://www.nuget.org/packages/ClosedXML/0.105.1
- https://www.nuget.org/packages/PDFsharp-MigraDoc-WPF/6.2.4
- https://www.nuget.org/packages/Tesseract/5.2.0
- https://www.nuget.org/packages/PDFtoImage/5.4.0
- https://www.nuget.org/packages/IxMilia.Dxf/0.8.4
- https://www.nuget.org/packages/Microsoft.Data.Sqlite/8.0.33

The PDF report dependency is intentionally pinned to PDFsharp/MigraDoc 6.2.4. Do not upgrade it to a version with different licensing terms without reviewing the package's current license and compatibility first.

This file lists direct application package dependencies. Transitive package notices remain governed by their respective package licenses.
