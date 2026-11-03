using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IxMilia.Dxf;
using IxMilia.Dxf.Entities;

namespace WAT.DrawingInspector;

public sealed class DxfScene
{
    internal List<DxfPrimitive> Primitives { get; } = new();
    public double MinX { get; internal set; }
    public double MinY { get; internal set; }
    public double MaxX { get; internal set; }
    public double MaxY { get; internal set; }
    public int SupportedEntityCount { get; internal set; }
    public int UnsupportedEntityCount { get; internal set; }
}

internal abstract record DxfPrimitive;
internal sealed record DxfLinePrimitive(double X1, double Y1, double X2, double Y2) : DxfPrimitive;
internal sealed record DxfCirclePrimitive(double X, double Y, double Radius) : DxfPrimitive;
internal sealed record DxfArcPrimitive(double X, double Y, double Radius, double StartAngle, double EndAngle) : DxfPrimitive;
internal sealed record DxfTextPrimitive(double X, double Y, string Value, double Height, double Rotation) : DxfPrimitive;

public sealed record DxfRenderResult(byte[] PngBytes, int PixelWidth, int PixelHeight,
    int SupportedEntityCount, int UnsupportedEntityCount);

public static class DxfDrawingService
{
    public static DxfScene LoadScene(string path)
    {
        var file = DxfFile.Load(path);
        var bounds = file.GetBoundingBox();
        var scene = new DxfScene
        {
            MinX = bounds.MinimumPoint.X,
            MinY = bounds.MinimumPoint.Y,
            MaxX = bounds.MaximumPoint.X,
            MaxY = bounds.MaximumPoint.Y
        };

        foreach (var entity in file.Entities)
        {
            switch (entity)
            {
                case DxfLine line:
                    scene.Primitives.Add(new DxfLinePrimitive(
                        line.P1.X, line.P1.Y, line.P2.X, line.P2.Y));
                    scene.SupportedEntityCount++;
                    break;

                case DxfArc arc:
                    scene.Primitives.Add(new DxfArcPrimitive(
                        arc.Center.X, arc.Center.Y, arc.Radius, arc.StartAngle, arc.EndAngle));
                    scene.SupportedEntityCount++;
                    break;

                case DxfCircle circle:
                    scene.Primitives.Add(new DxfCirclePrimitive(
                        circle.Center.X, circle.Center.Y, Math.Abs(circle.Radius)));
                    scene.SupportedEntityCount++;
                    break;

                case DxfLwPolyline polyline:
                    foreach (var part in polyline.AsSimpleEntities())
                        AddSimpleEntity(scene, part);
                    scene.SupportedEntityCount++;
                    break;

                case DxfPolyline polyline:
                    foreach (var part in polyline.AsSimpleEntities())
                        AddSimpleEntity(scene, part);
                    scene.SupportedEntityCount++;
                    break;

                case DxfText text:
                    scene.Primitives.Add(new DxfTextPrimitive(
                        text.Location.X, text.Location.Y, text.Value,
                        Math.Max(0.1, Math.Abs(text.TextHeight)), text.Rotation));
                    scene.SupportedEntityCount++;
                    break;

                default:
                    scene.UnsupportedEntityCount++;
                    break;
            }
        }

        if (scene.Primitives.Count == 0)
            throw new InvalidDataException("DXF dosyasında bu sürümün görüntüleyebildiği LINE, CIRCLE, ARC, POLYLINE veya TEXT geometrisi bulunamadı.");

        ExpandBoundsForPrimitives(scene);
        return scene;
    }

    private static void AddSimpleEntity(DxfScene scene, DxfEntity entity)
    {
        switch (entity)
        {
            case DxfLine line:
                scene.Primitives.Add(new DxfLinePrimitive(line.P1.X, line.P1.Y, line.P2.X, line.P2.Y));
                break;
            case DxfArc arc:
                scene.Primitives.Add(new DxfArcPrimitive(arc.Center.X, arc.Center.Y, arc.Radius, arc.StartAngle, arc.EndAngle));
                break;
        }
    }

    private static void ExpandBoundsForPrimitives(DxfScene scene)
    {
        var xs = new List<double>();
        var ys = new List<double>();

        void Add(double x, double y)
        {
            if (double.IsFinite(x) && double.IsFinite(y))
            {
                xs.Add(x);
                ys.Add(y);
            }
        }

        foreach (var primitive in scene.Primitives)
        {
            switch (primitive)
            {
                case DxfLinePrimitive line:
                    Add(line.X1, line.Y1);
                    Add(line.X2, line.Y2);
                    break;
                case DxfCirclePrimitive circle:
                    Add(circle.X - circle.Radius, circle.Y - circle.Radius);
                    Add(circle.X + circle.Radius, circle.Y + circle.Radius);
                    break;
                case DxfArcPrimitive arc:
                    var end = arc.EndAngle;
                    if (end <= arc.StartAngle) end += 360.0;
                    var steps = Math.Max(2, (int)Math.Ceiling((end - arc.StartAngle) / 5.0));
                    for (var i = 0; i <= steps; i++)
                    {
                        var angle = (arc.StartAngle + (end - arc.StartAngle) * i / steps) * Math.PI / 180.0;
                        Add(arc.X + arc.Radius * Math.Cos(angle), arc.Y + arc.Radius * Math.Sin(angle));
                    }
                    break;
                case DxfTextPrimitive text:
                    Add(text.X, text.Y);
                    break;
            }
        }

        if (xs.Count == 0) return;
        scene.MinX = xs.Min();
        scene.MaxX = xs.Max();
        scene.MinY = ys.Min();
        scene.MaxY = ys.Max();
    }

    public static DxfRenderResult RenderPng(DxfScene scene)
    {
        const double margin = 40.0;
        const double desiredPixelsPerUnit = 5.0;
        const double maxDimension = 5000.0;

        var worldWidth = Math.Max(1.0, scene.MaxX - scene.MinX);
        var worldHeight = Math.Max(1.0, scene.MaxY - scene.MinY);
        var scale = Math.Min(desiredPixelsPerUnit,
            Math.Min((maxDimension - 2 * margin) / worldWidth,
                     (maxDimension - 2 * margin) / worldHeight));

        var width = Math.Clamp((int)Math.Ceiling(worldWidth * scale + 2 * margin), 256, (int)maxDimension);
        var height = Math.Clamp((int)Math.Ceiling(worldHeight * scale + 2 * margin), 256, (int)maxDimension);

        Point Map(double x, double y) =>
            new(margin + (x - scene.MinX) * scale,
                margin + (scene.MaxY - y) * scale);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            var pen = new Pen(Brushes.DarkSlateGray, 0.85) { LineJoin = PenLineJoin.Round };
            pen.Freeze();

            foreach (var primitive in scene.Primitives)
            {
                switch (primitive)
                {
                    case DxfLinePrimitive line:
                        dc.DrawLine(pen, Map(line.X1, line.Y1), Map(line.X2, line.Y2));
                        break;

                    case DxfCirclePrimitive circle:
                        var center = Map(circle.X, circle.Y);
                        var radius = circle.Radius * scale;
                        dc.DrawEllipse(null, pen, center, radius, radius);
                        break;

                    case DxfArcPrimitive arc:
                        DrawArc(dc, pen, arc, Map, scale);
                        break;

                    case DxfTextPrimitive text:
                        var point = Map(text.X, text.Y);
                        var formatted = new FormattedText(
                            text.Value,
                            System.Globalization.CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight,
                            new Typeface("Arial"),
                            Math.Clamp(text.Height * scale, 5, 160),
                            Brushes.Black,
                            1.0);
                        dc.PushTransform(new RotateTransform(-text.Rotation, point.X, point.Y));
                        dc.DrawText(formatted, point);
                        dc.Pop();
                        break;
                }
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);

        return new DxfRenderResult(output.ToArray(), width, height,
            scene.SupportedEntityCount, scene.UnsupportedEntityCount);
    }

    private static void DrawArc(DrawingContext dc, Pen pen, DxfArcPrimitive arc,
        Func<double, double, Point> map, double scale)
    {
        var end = arc.EndAngle;
        if (end <= arc.StartAngle) end += 360.0;
        var sweep = end - arc.StartAngle;
        var steps = Math.Clamp((int)Math.Ceiling(sweep / 3.0), 2, 240);
        var points = new List<Point>(steps + 1);

        for (var i = 0; i <= steps; i++)
        {
            var angle = (arc.StartAngle + sweep * i / steps) * Math.PI / 180.0;
            points.Add(map(
                arc.X + arc.Radius * Math.Cos(angle),
                arc.Y + arc.Radius * Math.Sin(angle)));
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], false, false);
            context.PolyLineTo(points.Skip(1).ToList(), true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}