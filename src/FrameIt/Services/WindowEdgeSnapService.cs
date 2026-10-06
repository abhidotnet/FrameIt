using System.Diagnostics;
using System.Drawing;
using FrameIt.Interop;

namespace FrameIt.Services;

public sealed class WindowEdgeSnapService
{
    public IReadOnlyList<Rectangle> GetCandidateWindowBounds()
    {
        var currentPid = (uint)Process.GetCurrentProcess().Id;
        var windows = new List<Rectangle>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
            {
                return true;
            }

            if (NativeMethods.GetWindowTextLength(hWnd) <= 0)
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
            if (pid == currentPid)
            {
                return true;
            }

            if (NativeMethods.GetWindowRect(hWnd, out var rect))
            {
                var bounds = rect.ToRectangle();
                if (bounds.Width > 50 && bounds.Height > 50)
                {
                    windows.Add(bounds);
                }
            }

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    public Point SnapPoint(Point input, IReadOnlyList<Rectangle> candidates, int threshold)
    {
        var snappedX = input.X;
        var snappedY = input.Y;

        foreach (var rect in candidates)
        {
            snappedX = SnapAxis(snappedX, rect.Left, threshold);
            snappedX = SnapAxis(snappedX, rect.Right, threshold);
            snappedY = SnapAxis(snappedY, rect.Top, threshold);
            snappedY = SnapAxis(snappedY, rect.Bottom, threshold);
        }

        return new Point(snappedX, snappedY);
    }

    private static int SnapAxis(int value, int target, int threshold)
    {
        return Math.Abs(value - target) <= threshold ? target : value;
    }
}
