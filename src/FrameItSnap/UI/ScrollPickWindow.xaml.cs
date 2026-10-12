using System.Windows;
using System.Windows.Threading;
using Media = System.Windows.Media;
using WpfPoint = System.Windows.Point;
using FrameItSnap.Interop;
using FrameItSnap.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using WpfInput = System.Windows.Input;
using WpfRect = System.Windows.Rect;

namespace FrameItSnap.UI;

public partial class ScrollPickWindow : Window
{
    private readonly Drawing.Rectangle _monitor;
    private readonly PickSession _session;
    private double _scaleX = 1;
    private double _scaleY = 1;
    private bool _pinning;

    private ScrollPickWindow(Drawing.Rectangle monitor, PickSession session)
    {
        InitializeComponent();
        _monitor = monitor;
        _session = session;
        var scale = ScaleFor(monitor);
        _scaleX = scale.X;
        _scaleY = scale.Y;
        Width = CaptureGeometry.DipLength(monitor.Width, _scaleX * 96.0);
        Height = CaptureGeometry.DipLength(monitor.Height, _scaleY * 96.0);
        _session.Changed += () => InvalidateVisual();
        Loaded += (_, _) => PinToMonitor();
    }

    internal static ScrollSelection? Pick()
    {
        var capture = new CaptureService();
        var virtualBounds = capture.GetVirtualScreenBounds();
        var monitors = Monitors(virtualBounds);
        var session = new PickSession();
        var windows = new List<ScrollPickWindow>(monitors.Count);
        foreach (var monitor in monitors)
        {
            windows.Add(new ScrollPickWindow(monitor, session));
        }

        var frame = new DispatcherFrame();
        session.Finished += () => frame.Continue = false;
        EscapeKeyHook? hook = null;
        try
        {
            try
            {
                var dispatcher = windows[0].Dispatcher;
                hook = new EscapeKeyHook(() =>
                {
                    if (dispatcher.CheckAccess())
                    {
                        session.Cancel();
                    }
                    else
                    {
                        dispatcher.BeginInvoke(session.Cancel);
                    }
                });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                hook = null;
            }

            foreach (var window in windows)
            {
                window.Closed += (_, _) => session.Cancel();
                window.Show();
            }

            var focused = windows[0];
            var cursor = ReadCursor();
            foreach (var window in windows)
            {
                if (Contains(window._monitor, cursor))
                {
                    focused = window;
                    break;
                }
            }

            focused.Activate();
            focused.Focus();
            session.Move(cursor);
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            hook?.Dispose();
            foreach (var window in windows)
            {
                if (window.IsLoaded)
                {
                    window.Close();
                }
            }
        }

        return session.Selection;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        PinToMonitor();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (newDpi.DpiScaleX > 0 && newDpi.DpiScaleY > 0)
        {
            _scaleX = newDpi.DpiScaleX;
            _scaleY = newDpi.DpiScaleY;
            Width = CaptureGeometry.DipLength(_monitor.Width, _scaleX * 96.0);
            Height = CaptureGeometry.DipLength(_monitor.Height, _scaleY * 96.0);
        }

        PinToMonitor();
        InvalidateVisual();
    }

    protected override void OnRender(Media.DrawingContext dc)
    {
        base.OnRender(dc);
        var dest = new WpfRect(0, 0, Math.Max(Width, ActualWidth), Math.Max(Height, ActualHeight));
        var dim = new Media.SolidColorBrush(Media.Color.FromArgb(96, 0, 0, 0));
        dim.Freeze();
        if (_session.HitArea is not Drawing.Rectangle area)
        {
            dc.DrawRectangle(dim, null, dest);
            return;
        }

        var intersection = Drawing.Rectangle.Intersect(area, _monitor);
        if (intersection.Width < 1 || intersection.Height < 1)
        {
            dc.DrawRectangle(dim, null, dest);
            return;
        }

        var local = ToDip(intersection);
        DrawDimAround(dc, dim, dest, local);
        dc.DrawRectangle(null, new Media.Pen(Media.Brushes.DeepSkyBlue, 2), local);
        if (Contains(_monitor, new Drawing.Point(area.Left, area.Top)))
        {
            DrawLabel(dc, local);
        }
    }

    private void OnMouseMove(object sender, WpfInput.MouseEventArgs e)
    {
        _session.Move(ReadCursor());
    }

    private void OnMouseDown(object sender, WpfInput.MouseButtonEventArgs e)
    {
        if (e.RightButton == WpfInput.MouseButtonState.Pressed)
        {
            _session.Cancel();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == WpfInput.MouseButton.Left)
        {
            _session.Choose(ReadCursor());
            e.Handled = true;
        }
    }

    private void OnKeyDown(object sender, WpfInput.KeyEventArgs e)
    {
        if (e.Key == WpfInput.Key.Escape)
        {
            _session.Cancel();
            e.Handled = true;
        }
    }

    private void DrawLabel(Media.DrawingContext dc, WpfRect local)
    {
        var title = string.IsNullOrWhiteSpace(_session.Title) ? "Scrollable area" : _session.Title;
        var text = title + "  " + _session.HitArea!.Value.Width + " x " + _session.HitArea.Value.Height + "  — click";
        var formatted = new Media.FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Media.Typeface("Segoe UI"),
            14,
            Media.Brushes.White,
            Media.VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var width = Math.Min(formatted.Width + 12, Math.Max(40, LabelWidth(local)));
        var label = new WpfRect(local.X, Math.Max(0, local.Y - 28), width, 24);
        dc.DrawRectangle(new Media.SolidColorBrush(Media.Color.FromArgb(190, 0, 0, 0)), null, label);
        dc.PushClip(new Media.RectangleGeometry(label));
        dc.DrawText(formatted, new WpfPoint(label.X + 6, label.Y + 4));
        dc.Pop();
    }

    private double LabelWidth(WpfRect local)
    {
        return Math.Max(Width, ActualWidth) - local.X;
    }

    private static void DrawDimAround(Media.DrawingContext dc, Media.Brush dim, WpfRect dest, WpfRect hole)
    {
        var top = new WpfRect(dest.X, dest.Y, dest.Width, Math.Max(0, hole.Y - dest.Y));
        var bottomTop = hole.Bottom;
        var bottom = new WpfRect(dest.X, bottomTop, dest.Width, Math.Max(0, dest.Bottom - bottomTop));
        var left = new WpfRect(dest.X, hole.Y, Math.Max(0, hole.X - dest.X), hole.Height);
        var rightLeft = hole.Right;
        var right = new WpfRect(rightLeft, hole.Y, Math.Max(0, dest.Right - rightLeft), hole.Height);
        if (top.Height > 0)
        {
            dc.DrawRectangle(dim, null, top);
        }

        if (bottom.Height > 0)
        {
            dc.DrawRectangle(dim, null, bottom);
        }

        if (left.Width > 0)
        {
            dc.DrawRectangle(dim, null, left);
        }

        if (right.Width > 0)
        {
            dc.DrawRectangle(dim, null, right);
        }
    }

    private WpfRect ToDip(Drawing.Rectangle physical)
    {
        var dpiX = _scaleX * 96.0;
        var dpiY = _scaleY * 96.0;
        return new WpfRect(
            CaptureGeometry.DipLength(physical.Left - _monitor.Left, dpiX),
            CaptureGeometry.DipLength(physical.Top - _monitor.Top, dpiY),
            CaptureGeometry.DipLength(physical.Width, dpiX),
            CaptureGeometry.DipLength(physical.Height, dpiY));
    }

    private void PinToMonitor()
    {
        if (_pinning)
        {
            return;
        }

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        _pinning = true;
        try
        {
            NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HwndTopMost,
                _monitor.Left,
                _monitor.Top,
                _monitor.Width,
                _monitor.Height,
                NativeMethods.SwpNoActivate);
        }
        finally
        {
            _pinning = false;
        }
    }

    private static Drawing.Point ReadCursor()
    {
        if (NativeMethods.GetCursorPos(out var point))
        {
            return new Drawing.Point(point.X, point.Y);
        }

        return Forms.Cursor.Position;
    }

    private static bool Contains(Drawing.Rectangle rect, Drawing.Point point)
    {
        return point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom;
    }

    private static (double X, double Y) ScaleFor(Drawing.Rectangle monitor)
    {
        var probe = new NativeMethods.Point
        {
            X = monitor.Left + Math.Max(0, monitor.Width / 2),
            Y = monitor.Top + Math.Max(0, monitor.Height / 2)
        };
        var handle = NativeMethods.MonitorFromPoint(probe, NativeMethods.MonitorDefaultToNearest);
        if (handle != IntPtr.Zero &&
            NativeMethods.GetDpiForMonitor(handle, NativeMethods.MdtEffectiveDpi, out var dpiX, out var dpiY) == 0 &&
            dpiX >= 96 &&
            dpiY >= 96)
        {
            return (dpiX / 96.0, dpiY / 96.0);
        }

        return (1, 1);
    }

    private static IReadOnlyList<Drawing.Rectangle> Monitors(Drawing.Rectangle virtualBounds)
    {
        var monitors = new List<Drawing.Rectangle>();
        foreach (var screen in Forms.Screen.AllScreens)
        {
            if (screen.Bounds.Width > 0 && screen.Bounds.Height > 0)
            {
                monitors.Add(screen.Bounds);
            }
        }

        if (monitors.Count == 0)
        {
            monitors.Add(virtualBounds);
        }

        return monitors;
    }

    private sealed class PickSession
    {
        private bool _finished;

        public ScrollSelection? Selection { get; private set; }

        public Drawing.Rectangle? HitArea { get; private set; }

        public string Title { get; private set; } = string.Empty;

        public event Action? Changed;

        public event Action? Finished;

        public void Move(Drawing.Point cursor)
        {
            if (_finished)
            {
                return;
            }

            if (ScrollWindowLocator.TryHit(cursor, out var hit))
            {
                HitArea = hit.Client;
                Title = hit.Title;
            }
            else
            {
                HitArea = null;
                Title = string.Empty;
            }

            Changed?.Invoke();
        }

        public void Choose(Drawing.Point cursor)
        {
            if (_finished)
            {
                return;
            }

            if (!ScrollWindowLocator.TryHit(cursor, out var hit))
            {
                return;
            }

            var area = hit.Client;
            var x = Math.Clamp(cursor.X, area.Left, Math.Max(area.Left, area.Right - 1));
            var y = Math.Clamp(cursor.Y, area.Top, Math.Max(area.Top, area.Bottom - 1));
            Selection = new ScrollSelection(hit.Hwnd, hit.Root, area, new Drawing.Point(x, y));
            Finish();
        }

        public void Cancel()
        {
            if (_finished)
            {
                return;
            }

            Selection = null;
            Finish();
        }

        private void Finish()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            Finished?.Invoke();
        }
    }
}
