using System.Drawing;

namespace FrameIt.Editing;

public enum EditorTool
{
    Select,
    Crop,
    Arrow,
    Line,
    Rectangle,
    Ellipse,
    Pen,
    Highlighter,
    Text,
    Step,
    Blur,
    Pixelate
}

public enum AnnotationKind
{
    Arrow,
    Line,
    Rectangle,
    Ellipse,
    Pen,
    Highlighter,
    Text,
    Step
}

public readonly record struct PointD(double X, double Y);

public sealed class Annotation
{
    public AnnotationKind Kind { get; init; }

    public Color Color { get; set; } = Color.FromArgb(229, 57, 53);

    public float Thickness { get; set; } = 3;

    public float FontSize { get; set; } = 18;

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public double X2 { get; set; }

    public double Y2 { get; set; }

    public List<PointD> Points { get; set; } = new();

    public string Text { get; set; } = string.Empty;

    public int StepNumber { get; set; }

    public double TextWidth { get; set; }

    public double TextHeight { get; set; }

    public Annotation Clone()
    {
        return new Annotation
        {
            Kind = Kind,
            Color = Color,
            Thickness = Thickness,
            FontSize = FontSize,
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            X2 = X2,
            Y2 = Y2,
            Points = Points.Select(point => new PointD(point.X, point.Y)).ToList(),
            Text = Text,
            StepNumber = StepNumber,
            TextWidth = TextWidth,
            TextHeight = TextHeight
        };
    }

    public void Translate(double dx, double dy)
    {
        X += dx;
        Y += dy;
        X2 += dx;
        Y2 += dy;
        for (var index = 0; index < Points.Count; index++)
        {
            var point = Points[index];
            Points[index] = new PointD(point.X + dx, point.Y + dy);
        }
    }

    public void GetBounds(out double left, out double top, out double right, out double bottom)
    {
        switch (Kind)
        {
            case AnnotationKind.Line:
            case AnnotationKind.Arrow:
                var pad = Kind == AnnotationKind.Arrow ? Math.Max(10, Thickness * 4) : Thickness;
                left = Math.Min(X, X2) - pad;
                top = Math.Min(Y, Y2) - pad;
                right = Math.Max(X, X2) + pad;
                bottom = Math.Max(Y, Y2) + pad;
                return;
            case AnnotationKind.Rectangle:
            case AnnotationKind.Ellipse:
                left = X;
                top = Y;
                right = X + Width;
                bottom = Y + Height;
                return;
            case AnnotationKind.Pen:
            case AnnotationKind.Highlighter:
                if (Points.Count == 0)
                {
                    left = X;
                    top = Y;
                    right = X;
                    bottom = Y;
                    return;
                }

                left = Points.Min(point => point.X);
                top = Points.Min(point => point.Y);
                right = Points.Max(point => point.X);
                bottom = Points.Max(point => point.Y);
                var inflate = Math.Max(1, Thickness / 2);
                left -= inflate;
                top -= inflate;
                right += inflate;
                bottom += inflate;
                return;
            case AnnotationKind.Text:
                left = X;
                top = Y;
                right = X + Math.Max(4, TextWidth);
                bottom = Y + Math.Max(FontSize, TextHeight);
                return;
            case AnnotationKind.Step:
                var radius = StepRadius(FontSize);
                left = X - radius;
                top = Y - radius;
                right = X + radius;
                bottom = Y + radius;
                return;
            default:
                left = 0;
                top = 0;
                right = 0;
                bottom = 0;
                return;
        }
    }

    public bool HitTest(double x, double y)
    {
        const double slop = 6;
        switch (Kind)
        {
            case AnnotationKind.Line:
            case AnnotationKind.Arrow:
                return AnnotationGeometry.DistanceToSegment(x, y, X, Y, X2, Y2) <= Math.Max(slop, Thickness / 2 + 4);
            case AnnotationKind.Rectangle:
                return x >= X - slop && y >= Y - slop && x <= X + Width + slop && y <= Y + Height + slop;
            case AnnotationKind.Ellipse:
                var rx = Math.Max(1, Width / 2) + slop;
                var ry = Math.Max(1, Height / 2) + slop;
                var nx = (x - (X + Width / 2)) / rx;
                var ny = (y - (Y + Height / 2)) / ry;
                return nx * nx + ny * ny <= 1;
            case AnnotationKind.Pen:
            case AnnotationKind.Highlighter:
                var limit = Math.Max(slop, Thickness / 2 + 4);
                if (Points.Count == 0)
                {
                    return false;
                }

                if (Points.Count == 1)
                {
                    return AnnotationGeometry.Hypot(x - Points[0].X, y - Points[0].Y) <= limit;
                }

                for (var index = 1; index < Points.Count; index++)
                {
                    if (AnnotationGeometry.DistanceToSegment(
                            x,
                            y,
                            Points[index - 1].X,
                            Points[index - 1].Y,
                            Points[index].X,
                            Points[index].Y) <= limit)
                    {
                        return true;
                    }
                }

                return false;
            case AnnotationKind.Text:
            case AnnotationKind.Step:
                GetBounds(out var left, out var top, out var right, out var bottom);
                return x >= left - slop && y >= top - slop && x <= right + slop && y <= bottom + slop;
            default:
                return false;
        }
    }

    public bool Intersects(double left, double top, double right, double bottom)
    {
        GetBounds(out var itemLeft, out var itemTop, out var itemRight, out var itemBottom);
        return itemRight >= left && itemLeft <= right && itemBottom >= top && itemTop <= bottom;
    }

    public static double StepRadius(float fontSize) => Math.Max(12, fontSize * 0.7);

    public static bool UsesThickness(AnnotationKind kind)
    {
        return kind is AnnotationKind.Arrow
            or AnnotationKind.Line
            or AnnotationKind.Rectangle
            or AnnotationKind.Ellipse
            or AnnotationKind.Pen
            or AnnotationKind.Highlighter;
    }

    public static bool UsesFontSize(AnnotationKind kind)
    {
        return kind is AnnotationKind.Text or AnnotationKind.Step;
    }
}

public sealed class Redaction
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public bool Pixelate { get; set; }

    public int Strength { get; set; } = 8;

    public Redaction Clone()
    {
        return new Redaction
        {
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            Pixelate = Pixelate,
            Strength = Strength
        };
    }

    public void Translate(double dx, double dy)
    {
        X += dx;
        Y += dy;
    }

    public bool HitTest(double x, double y)
    {
        return x >= X && y >= Y && x <= X + Width && y <= Y + Height;
    }

    public bool Intersects(double left, double top, double right, double bottom)
    {
        return X + Width >= left && X <= right && Y + Height >= top && Y <= bottom;
    }

    public Rectangle ToPixelRect(int imageWidth, int imageHeight)
    {
        var left = (int)Math.Floor(X);
        var top = (int)Math.Floor(Y);
        var right = (int)Math.Ceiling(X + Width);
        var bottom = (int)Math.Ceiling(Y + Height);
        var rect = Rectangle.FromLTRB(left, top, right, bottom);
        return Rectangle.Intersect(rect, new Rectangle(0, 0, imageWidth, imageHeight));
    }
}

public static class AnnotationGeometry
{
    public static double Hypot(double x, double y) => Math.Sqrt((x * x) + (y * y));

    public static double DistanceToSegment(double px, double py, double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 0.0001)
        {
            return Hypot(px - x1, py - y1);
        }

        var t = Math.Clamp(((px - x1) * dx + (py - y1) * dy) / lengthSquared, 0, 1);
        var qx = x1 + t * dx;
        var qy = y1 + t * dy;
        return Hypot(px - qx, py - qy);
    }

    public static (PointD Tip, PointD Left, PointD Right, PointD LineEnd) ArrowHead(
        double x1,
        double y1,
        double x2,
        double y2,
        double thickness)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.001)
        {
            var tip = new PointD(x2, y2);
            return (tip, tip, tip, tip);
        }

        var ux = dx / length;
        var uy = dy / length;
        var head = Math.Max(10, thickness * 4);
        var wing = head * 0.45;
        var px = -uy;
        var py = ux;
        var baseX = x2 - ux * head;
        var baseY = y2 - uy * head;
        return (
            new PointD(x2, y2),
            new PointD(baseX + px * wing, baseY + py * wing),
            new PointD(baseX - px * wing, baseY - py * wing),
            new PointD(x2 - ux * head * 0.65, y2 - uy * head * 0.65));
    }

    public static (double X, double Y, double Width, double Height) NormalizeRect(double x1, double y1, double x2, double y2)
    {
        var x = Math.Min(x1, x2);
        var y = Math.Min(y1, y2);
        return (x, y, Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    }
}

public static class AnnotationTransforms
{
    public static List<Annotation> Clone(IEnumerable<Annotation> source)
    {
        return source.Select(item => item.Clone()).ToList();
    }

    public static List<Redaction> Clone(IEnumerable<Redaction> source)
    {
        return source.Select(item => item.Clone()).ToList();
    }

    public static Annotation Map(Annotation source, Func<double, double, (double X, double Y)> map)
    {
        var clone = source.Clone();
        switch (source.Kind)
        {
            case AnnotationKind.Arrow:
            case AnnotationKind.Line:
                (clone.X, clone.Y) = map(source.X, source.Y);
                (clone.X2, clone.Y2) = map(source.X2, source.Y2);
                break;
            case AnnotationKind.Rectangle:
            case AnnotationKind.Ellipse:
                var box = MapBox(source.X, source.Y, source.Width, source.Height, map);
                clone.X = box.X;
                clone.Y = box.Y;
                clone.Width = box.Width;
                clone.Height = box.Height;
                break;
            case AnnotationKind.Pen:
            case AnnotationKind.Highlighter:
                for (var index = 0; index < clone.Points.Count; index++)
                {
                    var point = source.Points[index];
                    var mapped = map(point.X, point.Y);
                    clone.Points[index] = new PointD(mapped.X, mapped.Y);
                }

                break;
            case AnnotationKind.Text:
            case AnnotationKind.Step:
                (clone.X, clone.Y) = map(source.X, source.Y);
                break;
        }

        return clone;
    }

    public static List<Annotation> MapAll(IReadOnlyList<Annotation> source, Func<double, double, (double X, double Y)> map)
    {
        return source.Select(item => Map(item, map)).ToList();
    }

    public static List<Annotation> Scale(IReadOnlyList<Annotation> source, double scaleX, double scaleY)
    {
        var average = (Math.Abs(scaleX) + Math.Abs(scaleY)) / 2;
        var scaled = new List<Annotation>(source.Count);
        foreach (var item in source)
        {
            var clone = Map(item, (x, y) => (x * scaleX, y * scaleY));
            clone.Thickness = (float)Math.Max(1, item.Thickness * average);
            clone.FontSize = (float)Math.Max(8, item.FontSize * average);
            clone.TextWidth = item.TextWidth * Math.Abs(scaleX);
            clone.TextHeight = item.TextHeight * Math.Abs(scaleY);
            scaled.Add(clone);
        }

        return scaled;
    }

    public static List<Annotation> Crop(IReadOnlyList<Annotation> source, double cropX, double cropY, double cropWidth, double cropHeight)
    {
        var kept = new List<Annotation>();
        var right = cropX + cropWidth;
        var bottom = cropY + cropHeight;
        foreach (var item in source)
        {
            if (!item.Intersects(cropX, cropY, right, bottom))
            {
                continue;
            }

            kept.Add(Map(item, (x, y) => (x - cropX, y - cropY)));
        }

        return kept;
    }

    public static List<Redaction> MapRedactions(IReadOnlyList<Redaction> source, Func<double, double, (double X, double Y)> map)
    {
        var mapped = new List<Redaction>(source.Count);
        foreach (var item in source)
        {
            var box = MapBox(item.X, item.Y, item.Width, item.Height, map);
            var clone = item.Clone();
            clone.X = box.X;
            clone.Y = box.Y;
            clone.Width = box.Width;
            clone.Height = box.Height;
            mapped.Add(clone);
        }

        return mapped;
    }

    public static List<Redaction> ScaleRedactions(IReadOnlyList<Redaction> source, double scaleX, double scaleY)
    {
        var average = (Math.Abs(scaleX) + Math.Abs(scaleY)) / 2;
        var scaled = new List<Redaction>(source.Count);
        foreach (var item in source)
        {
            var clone = item.Clone();
            clone.X = item.X * scaleX;
            clone.Y = item.Y * scaleY;
            clone.Width = item.Width * Math.Abs(scaleX);
            clone.Height = item.Height * Math.Abs(scaleY);
            clone.Strength = Math.Clamp((int)Math.Round(item.Strength * average), 1, 20);
            scaled.Add(clone);
        }

        return scaled;
    }

    public static List<Redaction> CropRedactions(IReadOnlyList<Redaction> source, double cropX, double cropY, double cropWidth, double cropHeight)
    {
        var kept = new List<Redaction>();
        var right = cropX + cropWidth;
        var bottom = cropY + cropHeight;
        foreach (var item in source)
        {
            if (!item.Intersects(cropX, cropY, right, bottom))
            {
                continue;
            }

            var clone = item.Clone();
            clone.X -= cropX;
            clone.Y -= cropY;
            kept.Add(clone);
        }

        return kept;
    }

    private static (double X, double Y, double Width, double Height) MapBox(
        double x,
        double y,
        double width,
        double height,
        Func<double, double, (double X, double Y)> map)
    {
        var corners = new[]
        {
            map(x, y),
            map(x + width, y),
            map(x, y + height),
            map(x + width, y + height)
        };

        var minX = corners.Min(corner => corner.X);
        var minY = corners.Min(corner => corner.Y);
        var maxX = corners.Max(corner => corner.X);
        var maxY = corners.Max(corner => corner.Y);
        return (minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
    }
}
