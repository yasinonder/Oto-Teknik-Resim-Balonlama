using System.IO;
using System.Windows.Media.Imaging;
using IxMilia.Dxf;
using IxMilia.Dxf.Blocks;
using IxMilia.Dxf.Entities;
using WAT.DrawingInspector;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wat-dxf-smoke-{Guid.NewGuid():N}.dxf");
        try
        {
            var file = new DxfFile();
            // LWPOLYLINE requires DXF R14 or newer.
            file.Header.Version = DxfAcadVersion.R2010;
            file.Entities.Add(new DxfLine(
                new DxfPoint(0, 0, 0),
                new DxfPoint(100, 60, 0)));
            file.Entities.Add(new DxfCircle(
                new DxfPoint(40, 30, 0), 12));
            file.Entities.Add(new DxfArc(
                new DxfPoint(70, 30, 0), 10, 20, 160));
            file.Entities.Add(new DxfLwPolyline(new[]
            {
                new DxfLwPolylineVertex { X = 5, Y = 5 },
                new DxfLwPolylineVertex { X = 25, Y = 5 },
                new DxfLwPolylineVertex { X = 25, Y = 20 }
            }));
            file.Entities.Add(new DxfText(
                new DxfPoint(10, 40, 0), 3.0, "WAT TEST"));

            var block = new DxfBlock { Name = "WAT-SYMBOL" };
            block.BasePoint = new DxfPoint(0, 0, 0);
            block.Entities.Add(new DxfLine(
                new DxfPoint(0, 0, 0),
                new DxfPoint(20, 0, 0)));
            block.Entities.Add(new DxfCircle(
                new DxfPoint(10, 5, 0), 3));
            file.Blocks.Add(block);

            var insert = new DxfInsert
            {
                Name = "WAT-SYMBOL",
                Location = new DxfPoint(130, 20, 0),
                XScaleFactor = 1.5,
                YScaleFactor = 1.5,
                Rotation = 30
            };
            file.Entities.Add(insert);

            file.Save(path);

            var scene = DxfDrawingService.LoadScene(path);
            if (scene.SupportedEntityCount != 7 || scene.UnsupportedEntityCount != 0)
                throw new InvalidOperationException(
                    $"Expected 7 supported DXF entities and 0 skipped, got {scene.SupportedEntityCount} supported / {scene.UnsupportedEntityCount} skipped.");

            var rendered = DxfDrawingService.RenderPng(scene);
            if (rendered.PngBytes.Length < 100)
                throw new InvalidOperationException("DXF renderer returned an empty PNG.");

            using var stream = new MemoryStream(rendered.PngBytes);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            var frame = decoder.Frames[0];
            if (frame.PixelWidth != rendered.PixelWidth ||
                frame.PixelHeight != rendered.PixelHeight)
                throw new InvalidOperationException("Rendered PNG dimensions do not match the render result.");

            Console.WriteLine(
                $"DXF smoke test passed: {rendered.SupportedEntityCount} entities including a transformed block; " +
                $"{rendered.PixelWidth}x{rendered.PixelHeight}px; {rendered.PngBytes.Length} bytes.");
            return 0;
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}