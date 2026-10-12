namespace FrameItSnap.Services;

/// <summary>
/// Maps selection rectangles between WPF device-independent pixels and the
/// physical-pixel virtual screen. No scaling is applied when a physical
/// rectangle is placed into the stitched capture bitmap.
/// </summary>
public static class CaptureGeometry
{
    public readonly record struct PixelRect(int X, int Y, int Width, int Height)
    {
        public bool HasArea => Width > 0 && Height > 0;
    }

    /// <summary>
    /// Layout length, in DIPs, of <paramref name="pixels"/> on a monitor whose
    /// DPI is <paramref name="dpi"/>. At that size, WPF's device transform puts
    /// one image pixel on one physical pixel (100% zoom).
    /// </summary>
    public static double DipLength(int pixels, double dpi)
    {
        var safeDpi = dpi >= 1 && !double.IsNaN(dpi) && !double.IsInfinity(dpi) ? dpi : 96;
        return pixels * 96.0 / safeDpi;
    }

    /// <summary>
    /// Converts a rectangle measured in DIPs on a single monitor into physical
    /// pixels. <paramref name="scaleX"/> and <paramref name="scaleY"/> are that
    /// monitor's DPI divided by 96 (1, 1.25, 1.5, 1.75). The origin of the DIP
    /// rectangle is the monitor's top-left corner. A selection that crosses
    /// monitors is already in physical pixels and must use <see cref="PhysicalToBitmap"/>
    /// instead of being scaled by one monitor.
    /// </summary>
    public static PixelRect DipToPhysical(
        int monitorLeft,
        int monitorTop,
        double scaleX,
        double scaleY,
        double dipX,
        double dipY,
        double dipWidth,
        double dipHeight)
    {
        if (!IsPositiveFinite(scaleX) || !IsPositiveFinite(scaleY))
        {
            throw new ArgumentOutOfRangeException(nameof(scaleX), "Monitor scale must be a positive finite number.");
        }

        if (dipWidth < 0)
        {
            dipX += dipWidth;
            dipWidth = -dipWidth;
        }

        if (dipHeight < 0)
        {
            dipY += dipHeight;
            dipHeight = -dipHeight;
        }

        var left = monitorLeft + Scale(dipX, scaleX);
        var top = monitorTop + Scale(dipY, scaleY);
        var right = monitorLeft + Scale(dipX + dipWidth, scaleX);
        var bottom = monitorTop + Scale(dipY + dipHeight, scaleY);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Places a physical-pixel selection into a bitmap captured 1:1 from the
    /// virtual screen. Width and height are copied, not scaled. Coordinates may
    /// be negative. The result is clipped to the bitmap and is null when the
    /// intersection is empty.
    /// </summary>
    public static PixelRect? PhysicalToBitmap(
        int selectionLeft,
        int selectionTop,
        int selectionWidth,
        int selectionHeight,
        int virtualLeft,
        int virtualTop,
        int bitmapWidth,
        int bitmapHeight)
    {
        if (selectionWidth < 1 || selectionHeight < 1 || bitmapWidth < 1 || bitmapHeight < 1)
        {
            return null;
        }

        var left = (long)selectionLeft - virtualLeft;
        var top = (long)selectionTop - virtualTop;
        var right = left + selectionWidth;
        var bottom = top + selectionHeight;
        var clipLeft = Math.Max(0L, left);
        var clipTop = Math.Max(0L, top);
        var clipRight = Math.Min((long)bitmapWidth, right);
        var clipBottom = Math.Min((long)bitmapHeight, bottom);
        if (clipRight <= clipLeft || clipBottom <= clipTop)
        {
            return null;
        }

        return new PixelRect(
            (int)clipLeft,
            (int)clipTop,
            (int)(clipRight - clipLeft),
            (int)(clipBottom - clipTop));
    }

    private static bool IsPositiveFinite(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static int Scale(double dip, double scale)
    {
        var value = dip * scale;
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(nameof(dip), "DIP coordinate is not finite.");
        }

        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}
