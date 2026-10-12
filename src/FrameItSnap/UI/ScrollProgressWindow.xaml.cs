using System.Windows;
using System.Windows.Interop;
using FrameItSnap.Interop;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace FrameItSnap.UI;

public partial class ScrollProgressWindow : Window
{
    public ScrollProgressWindow()
    {
        InitializeComponent();
    }

    public event EventHandler? StopRequested;

    public void SetProgress(int frames, int height, bool capped)
    {
        var text = frames + (frames == 1 ? " frame · " : " frames · ") + height + " px";
        if (capped)
        {
            text += " · height cap";
        }

        DetailText.Text = text;
    }

    public void PlaceAwayFrom(Drawing.Rectangle avoid)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        Drawing.Rectangle? best = null;
        var bestRank = int.MaxValue;
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var work = screen.WorkingArea;
            if (work.Width < 40 || work.Height < 40)
            {
                continue;
            }

            var scale = ScaleOf(work);
            var panelWidth = Math.Max(1, (int)Math.Round(Width * scale.X));
            var panelHeight = Math.Max(1, (int)Math.Round(Height * scale.Y));
            var spots = new[]
            {
                new Drawing.Rectangle(work.Right - panelWidth - 12, work.Top + 12, panelWidth, panelHeight),
                new Drawing.Rectangle(work.Left + 12, work.Top + 12, panelWidth, panelHeight),
                new Drawing.Rectangle(work.Right - panelWidth - 12, work.Bottom - panelHeight - 12, panelWidth, panelHeight),
                new Drawing.Rectangle(work.Left + 12, work.Bottom - panelHeight - 12, panelWidth, panelHeight)
            };
            foreach (var spot in spots)
            {
                var rank = (spot.IntersectsWith(avoid) ? 4 : 0) + (screen.Bounds.IntersectsWith(avoid) ? 2 : 0);
                if (rank < bestRank)
                {
                    bestRank = rank;
                    best = spot;
                }
            }
        }

        if (best is not Drawing.Rectangle panel)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HwndTopMost,
            panel.Left,
            panel.Top,
            panel.Width,
            panel.Height,
            NativeMethods.SwpNoActivate);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, new IntPtr(style));
    }

    private void StopButton_OnClick(object sender, RoutedEventArgs e)
    {
        StopRequested?.Invoke(this, EventArgs.Empty);
    }

    private static (double X, double Y) ScaleOf(Drawing.Rectangle area)
    {
        var probe = new NativeMethods.Point
        {
            X = area.Left + Math.Max(0, area.Width / 2),
            Y = area.Top + Math.Max(0, area.Height / 2)
        };
        var handle = NativeMethods.MonitorFromPoint(probe, NativeMethods.MonitorDefaultToNearest);
        try
        {
            if (handle != IntPtr.Zero &&
                NativeMethods.GetDpiForMonitor(handle, NativeMethods.MdtEffectiveDpi, out var dpiX, out var dpiY) == 0 &&
                dpiX >= 96 &&
                dpiY >= 96)
            {
                return (dpiX / 96.0, dpiY / 96.0);
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        return (1, 1);
    }
}
