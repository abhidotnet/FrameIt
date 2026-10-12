using System.ComponentModel;
using System.Drawing;
using FrameItSnap.UI;

namespace FrameItSnap.Services;

public static class ScrollCaptureSession
{
    private const int FocusSettleMilliseconds = 200;
    private const int WheelSettleMilliseconds = 280;
    private const int PageSettleMilliseconds = 320;
    private const int RepaintSettleMilliseconds = 160;

    public static async Task<Bitmap?> CaptureAsync(CaptureService capture, Action<string?> setBadge)
    {
        var selection = ScrollPickWindow.Pick();
        if (selection is null)
        {
            return null;
        }

        var area = Rectangle.Intersect(selection.Value.Client, capture.GetVirtualScreenBounds());
        if (area.Width < 32 || area.Height < 32)
        {
            return null;
        }

        ScrollWindowLocator.TryFocus(selection.Value.Root);

        using var cancellation = new CancellationTokenSource();
        EscapeKeyHook? hook = null;
        ScrollProgressWindow? progress = null;
        var stitcher = new ScrollStitch.Builder();
        try
        {
            try
            {
                hook = new EscapeKeyHook(() => cancellation.Cancel());
            }
            catch (Win32Exception)
            {
                hook = null;
            }

            progress = new ScrollProgressWindow();
            progress.StopRequested += (_, _) => cancellation.Cancel();
            progress.Show();
            progress.PlaceAwayFrom(area);
            setBadge("FrameIt Snap - scrolling");

            if (!await WaitAsync(FocusSettleMilliseconds, cancellation.Token))
            {
                return Finish(stitcher);
            }

            if (await GrabAsync(capture, area, stitcher, cancellation.Token) is null)
            {
                return null;
            }

            Report(progress, stitcher);
            while (!cancellation.IsCancellationRequested && !stitcher.HitHeightCap && stitcher.ResultHeight < ScrollStitch.MaxResultHeight)
            {
                var status = await ScrollStepAsync(capture, area, selection.Value, stitcher, cancellation.Token);
                Report(progress, stitcher);
                if (status != ScrollStitch.AddStatus.Extended)
                {
                    break;
                }
            }

            return Finish(stitcher);
        }
        catch (OperationCanceledException)
        {
            return Finish(stitcher);
        }
        finally
        {
            setBadge(null);
            hook?.Dispose();
            if (progress is not null)
            {
                progress.Close();
            }
        }
    }

    private static async Task<ScrollStitch.AddStatus> ScrollStepAsync(
        CaptureService capture,
        Rectangle area,
        ScrollSelection selection,
        ScrollStitch.Builder stitcher,
        CancellationToken cancellation)
    {
        ScrollWindowLocator.SendWheel(selection.Hwnd, selection.WheelPoint);
        if (!await WaitAsync(WheelSettleMilliseconds, cancellation))
        {
            return ScrollStitch.AddStatus.Unchanged;
        }

        var status = await GrabAsync(capture, area, stitcher, cancellation) ?? ScrollStitch.AddStatus.Unaligned;
        if (status == ScrollStitch.AddStatus.Unchanged)
        {
            if (!await WaitAsync(RepaintSettleMilliseconds, cancellation))
            {
                return status;
            }

            status = await GrabAsync(capture, area, stitcher, cancellation) ?? status;
        }

        if (status == ScrollStitch.AddStatus.Unchanged)
        {
            ScrollWindowLocator.SendPageDown(selection.Hwnd);
            if (!await WaitAsync(PageSettleMilliseconds, cancellation))
            {
                return status;
            }

            status = await GrabAsync(capture, area, stitcher, cancellation) ?? status;
            if (status == ScrollStitch.AddStatus.Unchanged)
            {
                if (!await WaitAsync(RepaintSettleMilliseconds, cancellation))
                {
                    return status;
                }

                status = await GrabAsync(capture, area, stitcher, cancellation) ?? status;
            }

            return status;
        }

        if (status == ScrollStitch.AddStatus.Unaligned)
        {
            if (!await WaitAsync(RepaintSettleMilliseconds, cancellation))
            {
                return status;
            }

            return await GrabAsync(capture, area, stitcher, cancellation) ?? status;
        }

        return status;
    }

    private static async Task<ScrollStitch.AddStatus?> GrabAsync(
        CaptureService capture,
        Rectangle area,
        ScrollStitch.Builder stitcher,
        CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested)
        {
            return null;
        }

        using var bitmap = capture.CapturePhysicalRegion(area);
        if (bitmap is null)
        {
            return null;
        }

        // Yield so the Stop button can be clicked between the blit and the next wheel.
        await Task.Yield();
        if (cancellation.IsCancellationRequested)
        {
            return null;
        }

        return stitcher.Add(ScrollFramePixels.FromBitmap(bitmap)).Status;
    }

    private static async Task<bool> WaitAsync(int milliseconds, CancellationToken cancellation)
    {
        try
        {
            await Task.Delay(milliseconds, cancellation);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static void Report(ScrollProgressWindow progress, ScrollStitch.Builder stitcher)
    {
        progress.SetProgress(stitcher.FrameCount, stitcher.ResultHeight, stitcher.HitHeightCap);
    }

    private static Bitmap? Finish(ScrollStitch.Builder stitcher)
    {
        var frame = stitcher.Build();
        if (frame.Width < 1 || frame.Height < 1)
        {
            return null;
        }

        return ScrollFramePixels.ToBitmap(frame);
    }
}
