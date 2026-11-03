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

## OCR language models

The Turkish `tur.traineddata` and English `eng.traineddata` files are obtained from the official [tesseract-ocr/tessdata_fast](https://github.com/tesseract-ocr/tessdata_fast) repository, pinned by `tools/Download-Tessdata.ps1` to commit `923915d4ced2a7235221788285785a29c4a42d4a`. The repository declares Apache-2.0 licensing. The models are not committed as binary files; the setup script downloads them into `WAT.DrawingInspector/tessdata`.

The `tessdata_fast` models are LSTM-only. The application therefore explicitly initializes Tesseract with `EngineMode.LstmOnly`. These fast models are a speed/accuracy compromise; OCR output and suggested dimensions must be reviewed by the operator before a balloon is accepted.

- Model source and license: https://github.com/tesseract-ocr/tessdata_fast
- Tesseract model documentation: https://tesseract-ocr.github.io/tessdoc/Data-Files

The PDF report dependency is intentionally pinned to PDFsharp/MigraDoc 6.2.4. Do not upgrade it to a version with different licensing terms without reviewing the package's current license and compatibility first.

This file lists direct application package dependencies. Transitive package notices remain governed by their respective package licenses.
