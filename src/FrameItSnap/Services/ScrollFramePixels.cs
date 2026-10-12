using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FrameItSnap.Services;

public static class ScrollFramePixels
{
    public static ScrollStitch.FrameView FromBitmap(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var pixels = new byte[width * height * 4];
        var bounds = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = width * 4;
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), pixels, y * rowBytes, rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return new ScrollStitch.FrameView(width, height, pixels);
    }

    public static Bitmap ToBitmap(ScrollStitch.FrameView frame)
    {
        if (frame.Width < 1 || frame.Height < 1 || frame.Pixels.Length < frame.Width * frame.Height * 4)
        {
            throw new ArgumentException("The stitched frame has no pixels.", nameof(frame));
        }

        var bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
        bitmap.SetResolution(96, 96);
        var bounds = new Rectangle(0, 0, frame.Width, frame.Height);
        var data = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = frame.Width * 4;
            for (var y = 0; y < frame.Height; y++)
            {
                Marshal.Copy(frame.Pixels, y * rowBytes, data.Scan0 + (y * data.Stride), rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }
}
