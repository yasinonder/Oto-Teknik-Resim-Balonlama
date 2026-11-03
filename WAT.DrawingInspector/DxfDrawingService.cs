using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IxMilia.Dxf;
using IxMilia.Dxf.Blocks;
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
    private const int MaxBlockDepth = 8;
    private const int MaxBlockArrayRowsOrColumns = 32;

    public static DxfScene LoadScene(string path)
    {
        var file = DxfFile.Load(path);
        var blocks = file.Blocks
            .GroupBy(block => block.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var scene = new DxfScene();
        var blockStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in file.Entities)
            ProcessEntity(scene, entity, DxfTransform.Identity, blocks, blockStack, depth: 0);

        if (scene.Primitives.Count == 0)
            throw new InvalidDataException("DXF dosyasında bu sürümün görüntüleyebildiği 2B geometri veya yazı bulunamadı.");

        ExpandBoundsForPrimitives(scene);
        return scene;
    }

    private static void ProcessEntity(
        DxfScene scene,
        DxfEntity entity,
        DxfTransform transform,
        IReadOnlyDictionary<string, DxfBlock> blocks,
        HashSet<string> blockStack,
        int depth,
        bool countAsEntity = true)
    {
        switch (entity)
        {
            case DxfLine line:
            {
                var p1 = transform.Transform(line.P1.X, line.P1.Y);
                var p2 = transform.Transform(line.P2.X, line.P2.Y);
                scene.Primitives.Add(new DxfLinePrimitive(p1.X, p1.Y, p2.X, p2.Y));
                if (countAsEntity) scene.SupportedEntityCount++;
                break;
            }

            case DxfArc arc:
                AddArc(scene, arc, transform);
                if (countAsEntity) scene.SupportedEntityCount++;
                break;

            case DxfCircle circle:
                AddCircle(scene, circle, transform);
                if (countAsEntity) scene.SupportedEntityCount++;
                break;

            case DxfLwPolyline polyline:
            {
                var before = scene.Primitives.Count;
                foreach (var part in polyline.AsSimpleEntities())
                    ProcessEntity(scene, part, transform, blocks, blockStack, depth, countAsEntity: false);

                if (countAsEntity)
                {
                    if (scene.Primitives.Count > before) scene.SupportedEntityCount++;
                    else scene.UnsupportedEntityCount++;
                }
                break;
            }

            case DxfPolyline polyline:
            {
                var before = scene.Primitives.Count;
                foreach (var part in polyline.AsSimpleEntities())
                    ProcessEntity(scene, part, transform, blocks, blockStack, depth, countAsEntity: false);

                if (countAsEntity)
                {
                    if (scene.Primitives.Count > before) scene.SupportedEntityCount++;
                    else scene.UnsupportedEntityCount++;
                }
                break;
            }

            case DxfText text:
            {
                var position = transform.Transform(text.Location.X, text.Location.Y);
                var angle = text.Rotation * Math.PI / 180.0;
                var direction = transform.TransformVector(Math.Cos(angle), Math.Sin(angle));
                var rotation = Math.Atan2(direction.Y, direction.X) * 180.0 / Math.PI;
                var height = Math.Max(0.1, Math.Abs(text.TextHeight) * transform.ScaleY);
                scene.Primitives.Add(new DxfTextPrimitive(position.X, position.Y, text.Value, height, rotation));
                if (countAsEntity) scene.SupportedEntityCount++;
                break;
            }

            case DxfInsert insert:
                ProcessInsert(scene, insert, transform, blocks, blockStack, depth);
                break;

            default:
                if (countAsEntity) scene.UnsupportedEntityCount++;
                break;
        }
    }

    private static void ProcessInsert(
        DxfScene scene,
        DxfInsert insert,
        DxfTransform parentTransform,
        IReadOnlyDictionary<string, DxfBlock> blocks,
        HashSet<string> blockStack,
        int depth)
    {
        if (depth >= MaxBlockDepth || string.IsNullOrWhiteSpace(insert.Name) ||
            !blocks.TryGetValue(insert.Name, out var block) ||
            !blockStack.Add(block.Name))
        {
            scene.UnsupportedEntityCount++;
            return;
        }

        try
        {
            var originalRows = Math.Max(1, (int)insert.RowCount);
            var originalColumns = Math.Max(1, (int)insert.ColumnCount);
            var rows = Math.Min(originalRows, MaxBlockArrayRowsOrColumns);
            var columns = Math.Min(originalColumns, MaxBlockArrayRowsOrColumns);
            if (rows != originalRows || columns != originalColumns)
                scene.UnsupportedEntityCount++;

            var angle = insert.Rotation * Math.PI / 180.0;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            var sx = insert.XScaleFactor;
            var sy = insert.YScaleFactor;

            var m11 = cos * sx;
            var m12 = -sin * sy;
            var m21 = sin * sx;
            var m22 = cos * sy;

            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var localX = column * insert.ColumnSpacing - block.BasePoint.X;
                    var localY = row * insert.RowSpacing - block.BasePoint.Y;

                    var dx = insert.Location.X + m11 * localX + m12 * localY;
                    var dy = insert.Location.Y + m21 * localX + m22 * localY;

                    var insertTransform = new DxfTransform(
                        m11, m12, m21, m22, dx, dy);
                    var worldTransform = parentTransform.Compose(insertTransform);

                    foreach (var child in block.Entities)
                    {
                        ProcessEntity(scene, child, worldTransform, blocks, blockStack, depth + 1);
                    }
                }
            }
        }
        finally
        {
            blockStack.Remove(block.Name);
        }
    }

    private static void AddCircle(DxfScene scene, DxfCircle circle, DxfTransform transform)
    {
        var center = transform.Transform(circle.Center.X, circle.Center.Y);
        var radius = Math.Abs(circle.Radius);

        if (transform.IsUniformScale)
        {
            scene.Primitives.Add(new DxfCirclePrimitive(
                center.X, center.Y, radius * transform.UniformScale));
            return;
        }

        AddCurveAsLines(scene, 0, 360, angle =>
        {
            var radians = angle * Math.PI / 180.0;
            return transform.Transform(
                circle.Center.X + radius * Math.Cos(radians),
                circle.Center.Y + radius * Math.Sin(radians));
        });
    }

    private static void AddArc(DxfScene scene, DxfArc arc, DxfTransform transform)
    {
        if (transform.IsIdentity)
        {
            scene.Primitives.Add(new DxfArcPrimitive(
                arc.Center.X, arc.Center.Y, Math.Abs(arc.Radius),
                arc.StartAngle, arc.EndAngle));
            return;
        }

        var end = arc.EndAngle;
        if (end <= arc.StartAngle) end += 360.0;
        AddCurveAsLines(scene, arc.StartAngle, end, angle =>
        {
            var radians = angle * Math.PI / 180.0;
            return transform.Transform(
                arc.Center.X + arc.Radius * Math.Cos(radians),
                arc.Center.Y + arc.Radius * Math.Sin(radians));
        });
    }

    private static void AddCurveAsLines(
        DxfScene scene,
        double startAngle,
        double endAngle,
        Func<double, DxfPoint2> pointAtAngle)
    {
        var sweep = endAngle - startAngle;
        var steps = Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / 4.0), 8, 180);
        var previous = pointAtAngle(startAngle);

        for (var i = 1; i <= steps; i++)
        {
            var current = pointAtAngle(startAngle + sweep * i / steps);
            scene.Primitives.Add(new DxfLinePrimitive(
                previous.X, previous.Y, current.X, current.Y));
            previous = current;
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
                {
                    var end = arc.EndAngle;
                    if (end <= arc.StartAngle) end += 360.0;
                    var steps = Math.Max(2, (int)Math.Ceiling((end - arc.StartAngle) / 5.0));
                    for (var i = 0; i <= steps; i++)
                    {
                        var angle = (arc.StartAngle + (end - arc.StartAngle) * i / steps) * Math.PI / 180.0;
                        Add(arc.X + arc.Radius * Math.Cos(angle), arc.Y + arc.Radius * Math.Sin(angle));
                    }
                    break;
                }

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
                        DrawArc(dc, pen, arc, Map);
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
        Func<double, double, Point> map)
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

    private readonly record struct DxfPoint2(double X, double Y);

    private readonly record struct DxfTransform(
        double M11, double M12, double M21, double M22, double DX, double DY)
    {
        public static DxfTransform Identity => new(1, 0, 0, 1, 0, 0);

        public bool IsIdentity =>
            M11 == 1 && M12 == 0 && M21 == 0 && M22 == 1 && DX == 0 && DY == 0;

        public double ScaleX => Math.Sqrt(M11 * M11 + M21 * M21);
        public double ScaleY => Math.Sqrt(M12 * M12 + M22 * M22);
        public double UniformScale => (ScaleX + ScaleY) * 0.5;

        public bool IsUniformScale =>
            Math.Abs(ScaleX - ScaleY) < 1e-8 &&
            Math.Abs(M11 * M12 + M21 * M22) < 1e-8;

        public DxfPoint2 Transform(double x, double y) =>
            new(M11 * x + M12 * y + DX, M21 * x + M22 * y + DY);

        public DxfPoint2 TransformVector(double x, double y) =>
            new(M11 * x + M12 * y, M21 * x + M22 * y);

        public DxfTransform Compose(DxfTransform child) =>
            new(
                M11 * child.M11 + M12 * child.M21,
                M11 * child.M12 + M12 * child.M22,
                M21 * child.M11 + M22 * child.M21,
                M21 * child.M12 + M22 * child.M22,
                M11 * child.DX + M12 * child.DY + DX,
                M21 * child.DX + M22 * child.DY + DY);
    }
}