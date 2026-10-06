using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameIt.Editing;
using FrameIt.Interop;
using FrameIt.Models;
using FrameIt.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using WpfInput = System.Windows.Input;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace FrameIt.UI;

public enum SelectionMode
{
    Freeform,
    FixedSize
}

public partial class RegionSelectionWindow : Window
{
    private readonly Drawing.Rectangle _monitor;
    private readonly SelectionSession _session;
    private readonly BitmapSource _frozen;
    private BitmapSource? _slice;
    private double _scaleX = 1;
    private double _scaleY = 1;
    private bool _pinning;

    private RegionSelectionWindow(Drawing.Rectangle monitor, SelectionSession session)
    {
        InitializeComponent();
        _monitor = monitor;
        _session = session;
        _frozen = session.Frozen;
        var scale = ScaleFor(monitor);
        _scaleX = scale.X;
        _scaleY = scale.Y;
        Width = monitor.Width / _scaleX;
        Height = monitor.Height / _scaleY;
        _session.Changed += () => InvalidateVisual();
        Loaded += (_, _) => PinToMonitor();
    }

    public static Drawing.Bitmap? CaptureRegion(
        WindowEdgeSnapService edgeSnapService,
        SelectionMode selectionMode,
        AppSettings settings)
    {
        var virtualBounds = Forms.SystemInformation.VirtualScreen;
        if (virtualBounds.Width < 2 || virtualBounds.Height < 2)
        {
            return null;
        }

        // Freeze the desktop before any overlay exists. The loupe is drawn later, on top of this
        // bitmap, and is never copied into it.
        using var desktop = new CaptureService().CaptureRectangle(virtualBounds);
        var frozen = BitmapInterop.ToBitmapSource(desktop);
        var session = new SelectionSession(desktop, frozen, virtualBounds, selectionMode, settings, edgeSnapService);
        var monitors = Monitors(virtualBounds);
        var windows = new List<RegionSelectionWindow>(monitors.Count);
        foreach (var monitor in monitors)
        {
            windows.Add(new RegionSelectionWindow(monitor, session));
        }

        var frame = new DispatcherFrame();
        session.Finished += () => frame.Continue = false;
        try
        {
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
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            foreach (var window in windows)
            {
                if (window.IsLoaded)
                {
                    window.Close();
                }
            }
        }

        return session.Result;
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
            Width = _monitor.Width / _scaleX;
            Height = _monitor.Height / _scaleY;
        }

        PinToMonitor();
        InvalidateVisual();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        base.OnClosed(e);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var slice = EnsureSlice();
        if (slice is null)
        {
            return;
        }

        var dest = new WpfRect(0, 0, slice.PixelWidth / _scaleX, slice.PixelHeight / _scaleY);
        dc.DrawImage(slice, dest);
        dc.DrawRectangle(
            new SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 0, 0, 0)),
            null,
            dest);

        if (_session.Selection is Drawing.Rectangle selection)
        {
            var intersection = Drawing.Rectangle.Intersect(selection, _monitor);
            if (intersection.Width > 0 && intersection.Height > 0)
            {
                var local = ToDip(intersection);
                DrawUndimmed(dc, slice, intersection, local);
                dc.DrawRectangle(null, new System.Windows.Media.Pen(System.Windows.Media.Brushes.DeepSkyBlue, 2), local);
                if (Contains(_monitor, new Drawing.Point(selection.Left, selection.Top)))
                {
                    DrawSelectionLabel(dc, selection, local);
                }
            }
        }

        DrawLoupe(dc);
    }

    private void OnMouseDown(object sender, WpfInput.MouseButtonEventArgs e)
    {
        if (e.RightButton == WpfInput.MouseButtonState.Pressed)
        {
            _session.Cancel();
            return;
        }

        _session.Begin(ReadCursor());
        if (!_session.IsFinished)
        {
            CaptureMouse();
        }

        e.Handled = true;
    }

    private void OnMouseMove(object sender, WpfInput.MouseEventArgs e)
    {
        _session.Move(ReadCursor());
    }

    private void OnMouseUp(object sender, WpfInput.MouseButtonEventArgs e)
    {
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        _session.End();
        e.Handled = true;
    }

    private void OnKeyDown(object sender, WpfInput.KeyEventArgs e)
    {
        if (e.Key == WpfInput.Key.Escape)
        {
            _session.Cancel();
            e.Handled = true;
        }
    }

    private BitmapSource? EnsureSlice()
    {
        if (_slice is not null)
        {
            return _slice;
        }

        var x = _monitor.Left - _session.VirtualBounds.Left;
        var y = _monitor.Top - _session.VirtualBounds.Top;
        if (x < 0 || y < 0 || x >= _frozen.PixelWidth || y >= _frozen.PixelHeight)
        {
            return null;
        }

        var width = Math.Min(_monitor.Width, _frozen.PixelWidth - x);
        var height = Math.Min(_monitor.Height, _frozen.PixelHeight - y);
        if (width < 1 || height < 1)
        {
            return null;
        }

        var cropped = new CroppedBitmap(_frozen, new Int32Rect(x, y, width, height));
        cropped.Freeze();
        _slice = cropped;
        return _slice;
    }

    private void DrawUndimmed(DrawingContext dc, BitmapSource slice, Drawing.Rectangle intersection, WpfRect local)
    {
        var x = intersection.Left - _monitor.Left;
        var y = intersection.Top - _monitor.Top;
        var width = Math.Min(intersection.Width, slice.PixelWidth - x);
        var height = Math.Min(intersection.Height, slice.PixelHeight - y);
        if (x < 0 || y < 0 || width < 1 || height < 1)
        {
            return;
        }

        var undimmed = new CroppedBitmap(slice, new Int32Rect(x, y, width, height));
        undimmed.Freeze();
        // DIP size is physical pixels divided by this monitor's scale, so the preview is not magnified.
        dc.DrawImage(undimmed, new WpfRect(local.X, local.Y, width / _scaleX, height / _scaleY));
    }

    private void DrawSelectionLabel(DrawingContext dc, Drawing.Rectangle selected, WpfRect localRect)
    {
        var text = selected.Width + " x " + selected.Height;
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            14,
            System.Windows.Media.Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        var labelRect = new WpfRect(localRect.X, Math.Max(0, localRect.Y - 28), formatted.Width + 12, 24);
        dc.DrawRectangle(
            new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 0, 0, 0)),
            null,
            labelRect);
        dc.DrawText(formatted, new WpfPoint(labelRect.X + 6, labelRect.Y + 4));
    }

    private void DrawLoupe(DrawingContext dc)
    {
        if (!Contains(_monitor, _session.Cursor))
        {
            return;
        }

        var sampleSize = Math.Min(28, Math.Min(_frozen.PixelWidth, _frozen.PixelHeight));
        if (sampleSize < 1)
        {
            return;
        }

        var bitmapX = _session.Cursor.X - _session.VirtualBounds.Left;
        var bitmapY = _session.Cursor.Y - _session.VirtualBounds.Top;
        var sampleLeft = Math.Clamp(bitmapX - (sampleSize / 2), 0, Math.Max(0, _frozen.PixelWidth - sampleSize));
        var sampleTop = Math.Clamp(bitmapY - (sampleSize / 2), 0, Math.Max(0, _frozen.PixelHeight - sampleSize));
        var cropped = new CroppedBitmap(_frozen, new Int32Rect(sampleLeft, sampleTop, sampleSize, sampleSize));
        cropped.Freeze();

        const double loupeSize = 140;
        var localCursor = ToDip(new Drawing.Rectangle(_session.Cursor.X, _session.Cursor.Y, 1, 1));
        var loupeX = localCursor.X + 28;
        var loupeY = localCursor.Y + 28;
        var limitWidth = Math.Max(Width, ActualWidth);
        var limitHeight = Math.Max(Height, ActualHeight);
        if (loupeX + loupeSize > limitWidth)
        {
            loupeX = localCursor.X - loupeSize - 28;
        }

        if (loupeY + loupeSize > limitHeight)
        {
            loupeY = localCursor.Y - loupeSize - 28;
        }

        loupeX = Math.Clamp(loupeX, 0, Math.Max(0, limitWidth - loupeSize));
        loupeY = Math.Clamp(loupeY, 0, Math.Max(0, limitHeight - loupeSize));
        var loupeRect = new WpfRect(loupeX, loupeY, loupeSize, loupeSize);
        dc.DrawRectangle(
            new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 20, 20, 20)),
            new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 1),
            loupeRect);
        dc.DrawImage(cropped, loupeRect);

        var centerX = loupeRect.Left + (loupeRect.Width / 2);
        var centerY = loupeRect.Top + (loupeRect.Height / 2);
        dc.DrawLine(
            new System.Windows.Media.Pen(System.Windows.Media.Brushes.Red, 1),
            new WpfPoint(centerX, loupeRect.Top),
            new WpfPoint(centerX, loupeRect.Bottom));
        dc.DrawLine(
            new System.Windows.Media.Pen(System.Windows.Media.Brushes.Red, 1),
            new WpfPoint(loupeRect.Left, centerY),
            new WpfPoint(loupeRect.Right, centerY));
    }

    private WpfRect ToDip(Drawing.Rectangle physical)
    {
        return new WpfRect(
            (physical.Left - _monitor.Left) / _scaleX,
            (physical.Top - _monitor.Top) / _scaleY,
            physical.Width / _scaleX,
            physical.Height / _scaleY);
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

    private sealed class SelectionSession
    {
        private readonly Drawing.Bitmap _desktop;
        private readonly WindowEdgeSnapService _edgeSnapService;
        private readonly SelectionMode _selectionMode;
        private readonly AppSettings _settings;
        private readonly IReadOnlyList<Drawing.Rectangle> _snapTargets;
        private bool _finished;
        private bool _dragging;
        private Drawing.Point _dragStart;

        public SelectionSession(
            Drawing.Bitmap desktop,
            BitmapSource frozen,
            Drawing.Rectangle virtualBounds,
            SelectionMode selectionMode,
            AppSettings settings,
            WindowEdgeSnapService edgeSnapService)
        {
            _desktop = desktop;
            Frozen = frozen;
            VirtualBounds = virtualBounds;
            _selectionMode = selectionMode;
            _settings = settings;
            _edgeSnapService = edgeSnapService;
            _snapTargets = edgeSnapService.GetCandidateWindowBounds();
            Cursor = ReadCursor();
        }

        public BitmapSource Frozen { get; }

        public Drawing.Rectangle VirtualBounds { get; }

        public Drawing.Point Cursor { get; private set; }

        public Drawing.Rectangle? Selection { get; private set; }

        public Drawing.Bitmap? Result { get; private set; }

        public bool IsFinished => _finished;

        public event Action? Changed;

        public event Action? Finished;

        public void Move(Drawing.Point physical)
        {
            if (_finished)
            {
                return;
            }

            Cursor = Snap(physical);
            if (_selectionMode == SelectionMode.FixedSize)
            {
                Selection = Clamp(new Drawing.Rectangle(
                    Cursor.X,
                    Cursor.Y,
                    _settings.FixedRegionWidth,
                    _settings.FixedRegionHeight));
            }
            else if (_dragging)
            {
                Selection = Normalize(_dragStart, Cursor);
            }

            Changed?.Invoke();
        }

        public void Begin(Drawing.Point physical)
        {
            if (_finished)
            {
                return;
            }

            var snapped = Snap(physical);
            Cursor = snapped;
            if (_selectionMode == SelectionMode.FixedSize)
            {
                var rect = Clamp(new Drawing.Rectangle(
                    snapped.X,
                    snapped.Y,
                    _settings.FixedRegionWidth,
                    _settings.FixedRegionHeight));
                if (rect.HasValue)
                {
                    Accept(rect.Value);
                }

                return;
            }

            _dragging = true;
            _dragStart = snapped;
            Selection = Drawing.Rectangle.Empty;
            Changed?.Invoke();
        }

        public void End()
        {
            if (_finished || _selectionMode != SelectionMode.Freeform || !_dragging)
            {
                return;
            }

            _dragging = false;
            if (Selection is Drawing.Rectangle rect && rect.Width >= 2 && rect.Height >= 2)
            {
                Accept(rect);
                return;
            }

            Selection = null;
            Changed?.Invoke();
        }

        public void Cancel()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            Result = null;
            Finished?.Invoke();
        }

        private void Accept(Drawing.Rectangle selection)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            try
            {
                Result = Crop(selection);
            }
            catch (ArgumentException)
            {
                Result = null;
            }

            Finished?.Invoke();
        }

        private Drawing.Bitmap? Crop(Drawing.Rectangle selection)
        {
            var local = new Drawing.Rectangle(
                selection.Left - VirtualBounds.Left,
                selection.Top - VirtualBounds.Top,
                selection.Width,
                selection.Height);
            local = Drawing.Rectangle.Intersect(local, new Drawing.Rectangle(0, 0, _desktop.Width, _desktop.Height));
            if (local.Width < 2 || local.Height < 2)
            {
                return null;
            }

            return ImageEffects.Crop(_desktop, local);
        }

        private Drawing.Point Snap(Drawing.Point physical)
        {
            // Window edges from GetWindowRect are device pixels. Snapping replaces a coordinate;
            // it does not scale the bitmap.
            return _edgeSnapService.SnapPoint(physical, _snapTargets, threshold: 12);
        }

        private Drawing.Rectangle Normalize(Drawing.Point a, Drawing.Point b)
        {
            var left = Math.Min(a.X, b.X);
            var top = Math.Min(a.Y, b.Y);
            var right = Math.Max(a.X, b.X);
            var bottom = Math.Max(a.Y, b.Y);
            return Clamp(Drawing.Rectangle.FromLTRB(left, top, right, bottom)) ?? Drawing.Rectangle.Empty;
        }

        private Drawing.Rectangle? Clamp(Drawing.Rectangle rect)
        {
            var left = Math.Max(rect.Left, VirtualBounds.Left);
            var top = Math.Max(rect.Top, VirtualBounds.Top);
            var right = Math.Min(rect.Right, VirtualBounds.Right);
            var bottom = Math.Min(rect.Bottom, VirtualBounds.Bottom);
            var width = right - left;
            var height = bottom - top;
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            return new Drawing.Rectangle(left, top, width, height);
        }
    }
}
