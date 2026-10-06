using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FrameIt.Interop;
using Windows.Graphics.Capture;

namespace FrameIt.Services;

public sealed class CaptureService
{
    public bool IsWindowsGraphicsCaptureSupported()
    {
        try
        {
            return GraphicsCaptureSession.IsSupported();
        }
        catch
        {
            return false;
        }
    }

    public Rectangle GetMonitorUnderCursorBounds()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? Rectangle.Empty;
        }

        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MonitorDefaultToNearest);
        var monitorInfo = new NativeMethods.MonitorInfo
        {
            CbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };

        if (monitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return monitorInfo.RcMonitor.ToRectangle();
        }

        return System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? Rectangle.Empty;
    }

    public Rectangle? GetActiveWindowBounds()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return null;
        }

        var bounds = rect.ToRectangle();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        return bounds;
    }

    public Bitmap CaptureRectangle(Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentException("Capture bounds must be greater than zero.", nameof(bounds));
        }

        var desktopDc = NativeMethods.GetDC(IntPtr.Zero);
        if (desktopDc == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to acquire desktop device context.");
        }

        IntPtr memoryDc = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldObject = IntPtr.Zero;

        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(desktopDc);
            if (memoryDc == IntPtr.Zero)
            {
                throw new InvalidOperationException("Unable to create memory device context.");
            }

            hBitmap = NativeMethods.CreateCompatibleBitmap(desktopDc, bounds.Width, bounds.Height);
            if (hBitmap == IntPtr.Zero)
            {
                throw new InvalidOperationException("Unable to allocate capture bitmap.");
            }

            oldObject = NativeMethods.SelectObject(memoryDc, hBitmap);

            var copied = NativeMethods.BitBlt(
                memoryDc,
                0,
                0,
                bounds.Width,
                bounds.Height,
                desktopDc,
                bounds.Left,
                bounds.Top,
                NativeMethods.Srccopy | NativeMethods.CaptureBlt);

            if (!copied)
            {
                throw new InvalidOperationException("BitBlt failed while capturing screen contents.");
            }

            using var temp = Image.FromHbitmap(hBitmap);
            // Copy device pixels exactly. GDI+ DrawImage / DrawImageUnscaled scale by the
            // bitmap's DPI metadata, which on a scaled display magnifies the shot and clips it.
            return CopyDevicePixels((Bitmap)temp);
        }
        finally
        {
            if (oldObject != IntPtr.Zero && memoryDc != IntPtr.Zero)
            {
                NativeMethods.SelectObject(memoryDc, oldObject);
            }

            if (hBitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(hBitmap);
            }

            if (memoryDc != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(memoryDc);
            }

            if (desktopDc != IntPtr.Zero)
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, desktopDc);
            }
        }
    }

    private static Bitmap CopyDevicePixels(Bitmap source)
    {
        var destination = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        destination.SetResolution(96, 96);
        var rect = new Rectangle(0, 0, source.Width, source.Height);
        var sourceData = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var destinationData = destination.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = source.Width * 4;
            var buffer = new byte[rowBytes];
            for (var y = 0; y < source.Height; y++)
            {
                Marshal.Copy(sourceData.Scan0 + (y * sourceData.Stride), buffer, 0, rowBytes);
                Marshal.Copy(buffer, 0, destinationData.Scan0 + (y * destinationData.Stride), rowBytes);
            }
        }
        finally
        {
            source.UnlockBits(sourceData);
            destination.UnlockBits(destinationData);
        }

        return destination;
    }
}
