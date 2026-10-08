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
            return bitmapSource;
        }
        finally
        {
            DeleteObject(hBitmap);
        }
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
