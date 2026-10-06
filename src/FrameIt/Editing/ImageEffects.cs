using System.Drawing;
using System.IO;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace FrameIt.Editing;

public static class ImageEffects
{
    public const int MaxDimension = 10000;

    public static Bitmap CloneArgb(Bitmap source)
    {
        var clone = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        clone.SetResolution(96, 96);
        if (source.PixelFormat is PixelFormat.Format32bppArgb or PixelFormat.Format32bppPArgb &&
            TryCopyRows(source, clone))
        {
            return clone;
        }

        using var graphics = Graphics.FromImage(clone);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(
            source,
            new Rectangle(0, 0, source.Width, source.Height),
            0,
            0,
            source.Width,
            source.Height,
            GraphicsUnit.Pixel);
        return clone;
    }

    public static Bitmap Crop(Bitmap source, Rectangle rect)
    {
        rect = Rectangle.Intersect(rect, new Rectangle(0, 0, source.Width, source.Height));
        if (rect.Width < 1 || rect.Height < 1)
        {
            throw new ArgumentException("Crop rectangle is empty.", nameof(rect));
        }

        var destination = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        destination.SetResolution(96, 96);
        var sourceData = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var destinationData = destination.LockBits(
            new Rectangle(0, 0, rect.Width, rect.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = rect.Width * 4;
            var buffer = new byte[rowBytes];
            for (var y = 0; y < rect.Height; y++)
            {
                Marshal.Copy(sourceData.Scan0 + y * sourceData.Stride, buffer, 0, rowBytes);
                Marshal.Copy(buffer, 0, destinationData.Scan0 + y * destinationData.Stride, rowBytes);
            }
        }
        finally
        {
            source.UnlockBits(sourceData);
            destination.UnlockBits(destinationData);
        }

        return destination;
    }

    public static Bitmap Resize(Bitmap source, int width, int height)
    {
        width = Math.Clamp(width, 1, MaxDimension);
        height = Math.Clamp(height, 1, MaxDimension);
        var destination = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        destination.SetResolution(96, 96);
        using var graphics = Graphics.FromImage(destination);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        graphics.DrawImage(
            source,
            new Rectangle(0, 0, width, height),
            0,
            0,
            source.Width,
            source.Height,
            GraphicsUnit.Pixel,
            attributes);
        return destination;
    }

    public static Bitmap Rotate(Bitmap source, bool clockwise)
    {
        var clone = CloneArgb(source);
        clone.RotateFlip(clockwise ? RotateFlipType.Rotate90FlipNone : RotateFlipType.Rotate270FlipNone);
        clone.SetResolution(96, 96);
        return clone;
    }

    public static Bitmap Flip(Bitmap source, bool horizontal)
    {
        var clone = CloneArgb(source);
        clone.RotateFlip(horizontal ? RotateFlipType.RotateNoneFlipX : RotateFlipType.RotateNoneFlipY);
        clone.SetResolution(96, 96);
        return clone;
    }

    public static Bitmap Adjust(Bitmap source, int brightness, int contrast)
    {
        brightness = Math.Clamp(brightness, -100, 100);
        contrast = Math.Clamp(contrast, -100, 100);
        var destination = CloneArgb(source);
        if (brightness == 0 && contrast == 0)
        {
            return destination;
        }

        var brightnessOffset = (int)Math.Round(brightness * 255.0 / 100.0);
        var factor = (259.0 * (contrast + 255.0)) / (255.0 * (259.0 - contrast));
        var rectangle = new Rectangle(0, 0, destination.Width, destination.Height);
        var data = destination.LockBits(rectangle, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * destination.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            for (var y = 0; y < destination.Height; y++)
            {
                var row = y * data.Stride;
                for (var x = 0; x < destination.Width; x++)
                {
                    var index = row + x * 4;
                    bytes[index] = AdjustChannel(bytes[index], brightnessOffset, factor);
                    bytes[index + 1] = AdjustChannel(bytes[index + 1], brightnessOffset, factor);
                    bytes[index + 2] = AdjustChannel(bytes[index + 2], brightnessOffset, factor);
                }
            }

            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            destination.UnlockBits(data);
        }

        return destination;
    }

    public static Bitmap CreatePatch(Bitmap source, Rectangle rect, bool pixelate, int strength)
    {
        var patch = Crop(source, rect);
        if (pixelate)
        {
            Pixelate(patch, strength);
        }
        else
        {
            Blur(patch, strength);
        }

        return patch;
    }

    public static void ApplyRedaction(Bitmap target, Redaction redaction)
    {
        var rect = redaction.ToPixelRect(target.Width, target.Height);
        if (rect.Width < 1 || rect.Height < 1)
        {
            return;
        }

        using var patch = CreatePatch(target, rect, redaction.Pixelate, redaction.Strength);
        using var graphics = Graphics.FromImage(target);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(patch, rect);
    }

    public static Bitmap Flatten(Bitmap source, IReadOnlyList<Redaction> redactions, IReadOnlyList<Annotation> annotations)
    {
        var output = CloneArgb(source);
        foreach (var redaction in redactions)
        {
            ApplyRedaction(output, redaction);
        }

        using var graphics = Graphics.FromImage(output);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        foreach (var annotation in annotations)
        {
            DrawAnnotation(graphics, annotation);
        }

        return output;
    }

    public static void Save(Bitmap bitmap, string path, int jpegQuality)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            SaveJpeg(bitmap, path, jpegQuality);
            return;
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    public static (double Width, double Height) MeasureText(string text, float fontSize)
    {
        using var font = CreateFont(fontSize, bold: false);
        using var bitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        var size = graphics.MeasureString(text, font);
        return (Math.Max(4, size.Width), Math.Max(fontSize, size.Height));
    }

    public static void DrawAnnotation(Graphics graphics, Annotation annotation)
    {
        using var pen = CreatePen(annotation);
        switch (annotation.Kind)
        {
            case AnnotationKind.Arrow:
                DrawArrow(graphics, pen, annotation);
                break;
            case AnnotationKind.Line:
                graphics.DrawLine(pen, (float)annotation.X, (float)annotation.Y, (float)annotation.X2, (float)annotation.Y2);
                break;
            case AnnotationKind.Rectangle:
                graphics.DrawRectangle(pen, (float)annotation.X, (float)annotation.Y, (float)annotation.Width, (float)annotation.Height);
                break;
            case AnnotationKind.Ellipse:
                graphics.DrawEllipse(pen, (float)annotation.X, (float)annotation.Y, (float)annotation.Width, (float)annotation.Height);
                break;
            case AnnotationKind.Pen:
            case AnnotationKind.Highlighter:
                DrawStroke(graphics, pen, annotation.Points);
                break;
            case AnnotationKind.Text:
                using (var font = CreateFont(annotation.FontSize, bold: false))
                using (var brush = new SolidBrush(annotation.Color))
                {
                    graphics.DrawString(annotation.Text, font, brush, (float)annotation.X, (float)annotation.Y);
                }

                break;
            case AnnotationKind.Step:
                DrawStep(graphics, annotation);
                break;
        }
    }

    private static bool TryCopyRows(Bitmap source, Bitmap destination)
    {
        var rect = new Rectangle(0, 0, source.Width, source.Height);
        BitmapData? sourceData = null;
        BitmapData? destinationData = null;
        try
        {
            sourceData = source.LockBits(rect, ImageLockMode.ReadOnly, source.PixelFormat);
            destinationData = destination.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var rowBytes = source.Width * 4;
            var buffer = new byte[rowBytes];
            for (var y = 0; y < source.Height; y++)
            {
                Marshal.Copy(sourceData.Scan0 + (y * sourceData.Stride), buffer, 0, rowBytes);
                Marshal.Copy(buffer, 0, destinationData.Scan0 + (y * destinationData.Stride), rowBytes);
            }

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or InvalidOperationException)
        {
            return false;
        }
        finally
        {
            if (sourceData is not null)
            {
                source.UnlockBits(sourceData);
            }

            if (destinationData is not null)
            {
                destination.UnlockBits(destinationData);
            }
        }
    }

    private static void Blur(Bitmap bitmap, int strength)
    {
        var radius = Math.Clamp(strength, 1, 20);
        BoxBlur(bitmap, radius);
        BoxBlur(bitmap, Math.Max(1, radius / 2));
    }

    private static void BoxBlur(Bitmap bitmap, int radius)
    {
        if (bitmap.Width < 1 || bitmap.Height < 1)
        {
            return;
        }

        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var temp = new byte[bytes.Length];
            BlurHorizontal(bytes, temp, bitmap.Width, bitmap.Height, data.Stride, radius);
            BlurVertical(temp, bytes, bitmap.Width, bitmap.Height, data.Stride, radius);
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void BlurHorizontal(byte[] source, byte[] destination, int width, int height, int stride, int radius)
    {
        var window = radius * 2 + 1;
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            var sumB = 0;
            var sumG = 0;
            var sumR = 0;
            var sumA = 0;
            for (var offset = -radius; offset <= radius; offset++)
            {
                var sample = row + Math.Clamp(offset, 0, width - 1) * 4;
                sumB += source[sample];
                sumG += source[sample + 1];
                sumR += source[sample + 2];
                sumA += source[sample + 3];
            }

            for (var x = 0; x < width; x++)
            {
                var index = row + x * 4;
                destination[index] = (byte)(sumB / window);
                destination[index + 1] = (byte)(sumG / window);
                destination[index + 2] = (byte)(sumR / window);
                destination[index + 3] = (byte)(sumA / window);

                var remove = row + Math.Clamp(x - radius, 0, width - 1) * 4;
                var add = row + Math.Clamp(x + radius + 1, 0, width - 1) * 4;
                sumB += source[add] - source[remove];
                sumG += source[add + 1] - source[remove + 1];
                sumR += source[add + 2] - source[remove + 2];
                sumA += source[add + 3] - source[remove + 3];
            }
        }
    }

    private static void BlurVertical(byte[] source, byte[] destination, int width, int height, int stride, int radius)
    {
        var window = radius * 2 + 1;
        for (var x = 0; x < width; x++)
        {
            var sumB = 0;
            var sumG = 0;
            var sumR = 0;
            var sumA = 0;
            for (var offset = -radius; offset <= radius; offset++)
            {
                var sample = Math.Clamp(offset, 0, height - 1) * stride + x * 4;
                sumB += source[sample];
                sumG += source[sample + 1];
                sumR += source[sample + 2];
                sumA += source[sample + 3];
            }

            for (var y = 0; y < height; y++)
            {
                var index = y * stride + x * 4;
                destination[index] = (byte)(sumB / window);
                destination[index + 1] = (byte)(sumG / window);
                destination[index + 2] = (byte)(sumR / window);
                destination[index + 3] = (byte)(sumA / window);

                var remove = Math.Clamp(y - radius, 0, height - 1) * stride + x * 4;
                var add = Math.Clamp(y + radius + 1, 0, height - 1) * stride + x * 4;
                sumB += source[add] - source[remove];
                sumG += source[add + 1] - source[remove + 1];
                sumR += source[add + 2] - source[remove + 2];
                sumA += source[add + 3] - source[remove + 3];
            }
        }
    }

    private static void Pixelate(Bitmap bitmap, int strength)
    {
        var block = Math.Clamp(1 + strength * 2, 2, 64);
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            for (var y0 = 0; y0 < bitmap.Height; y0 += block)
            {
                var y1 = Math.Min(y0 + block, bitmap.Height);
                for (var x0 = 0; x0 < bitmap.Width; x0 += block)
                {
                    var x1 = Math.Min(x0 + block, bitmap.Width);
                    long sumB = 0;
                    long sumG = 0;
                    long sumR = 0;
                    long sumA = 0;
                    var count = 0;
                    for (var y = y0; y < y1; y++)
                    {
                        var row = y * data.Stride;
                        for (var x = x0; x < x1; x++)
                        {
                            var index = row + x * 4;
                            sumB += bytes[index];
                            sumG += bytes[index + 1];
                            sumR += bytes[index + 2];
                            sumA += bytes[index + 3];
                            count++;
                        }
                    }

                    if (count == 0)
                    {
                        continue;
                    }

                    var blue = (byte)(sumB / count);
                    var green = (byte)(sumG / count);
                    var red = (byte)(sumR / count);
                    var alpha = (byte)(sumA / count);
                    for (var y = y0; y < y1; y++)
                    {
                        var row = y * data.Stride;
                        for (var x = x0; x < x1; x++)
                        {
                            var index = row + x * 4;
                            bytes[index] = blue;
                            bytes[index + 1] = green;
                            bytes[index + 2] = red;
                            bytes[index + 3] = alpha;
                        }
                    }
                }
            }

            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void SaveJpeg(Bitmap bitmap, string path, int quality)
    {
        quality = Math.Clamp(quality, 1, 100);
        var codec = Array.Find(
            ImageCodecInfo.GetImageEncoders(),
            candidate => candidate.FormatID == ImageFormat.Jpeg.Guid);
        if (codec is null)
        {
            throw new InvalidOperationException("The JPEG encoder is not available on this system.");
        }

        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);
        bitmap.Save(path, codec, parameters);
    }

    private static byte AdjustChannel(byte value, int brightnessOffset, double contrastFactor)
    {
        var adjusted = value + brightnessOffset;
        adjusted = (int)Math.Round(contrastFactor * (adjusted - 128) + 128);
        return (byte)Math.Clamp(adjusted, 0, 255);
    }

    private static Pen CreatePen(Annotation annotation)
    {
        var color = annotation.Kind == AnnotationKind.Highlighter
            ? Color.FromArgb(96, annotation.Color)
            : annotation.Color;
        var thickness = Math.Max(1, annotation.Thickness);
        return new Pen(color, thickness)
        {
            StartCap = LineCap.Round,
            EndCap = annotation.Kind == AnnotationKind.Arrow ? LineCap.Flat : LineCap.Round,
            LineJoin = LineJoin.Round
        };
    }

    private static void DrawArrow(Graphics graphics, Pen pen, Annotation annotation)
    {
        var head = AnnotationGeometry.ArrowHead(annotation.X, annotation.Y, annotation.X2, annotation.Y2, annotation.Thickness);
        graphics.DrawLine(
            pen,
            (float)annotation.X,
            (float)annotation.Y,
            (float)head.LineEnd.X,
            (float)head.LineEnd.Y);
        using var brush = new SolidBrush(pen.Color);
        graphics.FillPolygon(
            brush,
            new[]
            {
                new PointF((float)head.Tip.X, (float)head.Tip.Y),
                new PointF((float)head.Left.X, (float)head.Left.Y),
                new PointF((float)head.Right.X, (float)head.Right.Y)
            });
    }

    private static void DrawStroke(Graphics graphics, Pen pen, IReadOnlyList<PointD> points)
    {
        if (points.Count == 0)
        {
            return;
        }

        if (points.Count == 1)
        {
            var radius = Math.Max(1, pen.Width / 2);
            using var brush = new SolidBrush(pen.Color);
            graphics.FillEllipse(
                brush,
                (float)(points[0].X - radius),
                (float)(points[0].Y - radius),
                radius * 2,
                radius * 2);
            return;
        }

        var drawingPoints = new PointF[points.Count];
        for (var index = 0; index < points.Count; index++)
        {
            drawingPoints[index] = new PointF((float)points[index].X, (float)points[index].Y);
        }

        graphics.DrawLines(pen, drawingPoints);
    }

    private static void DrawStep(Graphics graphics, Annotation annotation)
    {
        var radius = (float)Annotation.StepRadius(annotation.FontSize);
        var bounds = new RectangleF((float)annotation.X - radius, (float)annotation.Y - radius, radius * 2, radius * 2);
        using var fill = new SolidBrush(annotation.Color);
        graphics.FillEllipse(fill, bounds);
        using var font = CreateFont(annotation.FontSize * 0.75f, bold: true);
        using var textBrush = new SolidBrush(ContrastColor(annotation.Color));
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString(annotation.StepNumber.ToString(), font, textBrush, bounds, format);
    }

    private static Font CreateFont(float size, bool bold)
    {
        var emSize = Math.Clamp(size, 6, 200);
        var style = bold ? FontStyle.Bold : FontStyle.Regular;
        try
        {
            return new Font("Segoe UI", emSize, style, GraphicsUnit.Pixel);
        }
        catch (ArgumentException)
        {
            return new Font(FontFamily.GenericSansSerif, emSize, style, GraphicsUnit.Pixel);
        }
    }

    private static Color ContrastColor(Color color)
    {
        var luminance = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
        return luminance > 160 ? Color.Black : Color.White;
    }
}
