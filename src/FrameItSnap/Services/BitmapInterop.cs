using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace FrameItSnap.Services;

public static class BitmapInterop
{
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    public static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bitmapSource.Freeze();
            // HBITMAP conversion can stamp the screen DPI onto the pixels. Saved files and the
            // clipboard stay at 96 DPI so viewers do not scale a 1:1 screenshot.
            return WithDpi(bitmapSource, 96, 96);
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    public static BitmapSource ForEditorDisplay(Bitmap bitmap, double monitorDpiX, double monitorDpiY)
    {
        // Metadata only. Pixel width and height stay the physical capture size, and WPF
        // then lays the image out at pixels * 96 / monitorDpi, which is one device pixel
        // per image pixel on that monitor.
        return WithDpi(ToBitmapSource(bitmap), NormalizeDpi(monitorDpiX), NormalizeDpi(monitorDpiY));
    }

    public static BitmapSource WithDpi(BitmapSource source, double dpiX, double dpiY)
    {
        dpiX = NormalizeDpi(dpiX);
        dpiY = NormalizeDpi(dpiY);
        if (Math.Abs(source.DpiX - dpiX) < 0.5 && Math.Abs(source.DpiY - dpiY) < 0.5)
        {
            return source;
        }

        var format = source.Format;
        var bitsPerPixel = format.BitsPerPixel;
        if (bitsPerPixel < 1 || source.PixelWidth < 1 || source.PixelHeight < 1)
        {
            return source;
        }

        var stride = ((source.PixelWidth * bitsPerPixel + 31) / 32) * 4;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);
        var copy = BitmapSource.Create(
            source.PixelWidth,
            source.PixelHeight,
            dpiX,
            dpiY,
            format,
            source.Palette,
            pixels,
            stride);
        copy.Freeze();
        return copy;
    }

    private static double NormalizeDpi(double dpi)
    {
        return dpi >= 1 && !double.IsNaN(dpi) && !double.IsInfinity(dpi) ? dpi : 96;
    }

    public static bool TrySetClipboard(BitmapSource source)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetImage(source);
                return true;
            }
            catch (Exception)
            {
                if (attempt == 3)
                {
                    return false;
                }

                Thread.Sleep(30 * (attempt + 1));
            }
        }

        return false;
    }
}
