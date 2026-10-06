using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameIt.Models;
using FrameIt.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using WpfRect = System.Windows.Rect;

namespace FrameIt.UI;

public enum SelectionMode
{
    Freeform,
    FixedSize
}

public partial class RegionSelectionWindow : Window
{
    private readonly WindowEdgeSnapService _edgeSnapService;
    private readonly SelectionMode _selectionMode;
    private readonly AppSettings _settings;
    private readonly Drawing.Rectangle _virtualBounds;
    private IReadOnlyList<Drawing.Rectangle> _snapTargets = Array.Empty<Drawing.Rectangle>();
    private BitmapSource? _background;
    private Drawing.Point _startScreen;
    private Drawing.Point _currentScreen;
    private Drawing.Rectangle? _selection;
    private bool _dragging;

    public RegionSelectionWindow(WindowEdgeSnapService edgeSnapService, SelectionMode selectionMode, AppSettings settings)
    {
        InitializeComponent();
        _edgeSnapService = edgeSnapService;
        _selectionMode = selectionMode;
        _settings = settings;
        _virtualBounds = Forms.SystemInformation.VirtualScreen;

        Left = _virtualBounds.Left;
        Top = _virtualBounds.Top;
        Width = _virtualBounds.Width;
        Height = _virtualBounds.Height;
        Loaded += OnLoaded;
    }

    public Drawing.Rectangle? SelectedRegion { get; private set; }

    public static Drawing.Rectangle? SelectRegion(
        WindowEdgeSnapService edgeSnapService,
        SelectionMode selectionMode,
        AppSettings settings)
    {
        var picker = new RegionSelectionWindow(edgeSnapService, selectionMode, settings);
        var accepted = picker.ShowDialog();
        return accepted == true ? picker.SelectedRegion : null;
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

        if (_background is null)
        {
            return;
        }

        dc.DrawImage(_background, new WpfRect(0, 0, ActualWidth, ActualHeight));
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)), null, new WpfRect(0, 0, ActualWidth, ActualHeight));

        if (_selection.HasValue)
        {
            var localRect = ToLocalRect(_selection.Value);
            DrawUndimmedSelection(dc, _selection.Value, localRect);
            dc.DrawRectangle(null, new Pen(Brushes.DeepSkyBlue, 2), localRect);
            DrawSelectionLabel(dc, _selection.Value, localRect);
        }

        DrawLoupe(dc);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _snapTargets = _edgeSnapService.GetCandidateWindowBounds();

        var capture = new CaptureService();
        using var bmp = capture.CaptureRectangle(_virtualBounds);
        _background = BitmapInterop.ToBitmapSource(bmp);
        _currentScreen = Forms.Cursor.Position;
        Focus();
        CaptureMouse();
        InvalidateVisual();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.RightButton == MouseButtonState.Pressed)
        {
            DialogResult = false;
            return;
        }

        var snapped = Snap(ToScreenPoint(e.GetPosition(this)));
        _currentScreen = snapped;

        if (_selectionMode == SelectionMode.FixedSize)
        {
            _selection = ClampToVirtualBounds(new Drawing.Rectangle(
                snapped.X,
                snapped.Y,
                _settings.FixedRegionWidth,
                _settings.FixedRegionHeight));
            SelectedRegion = _selection;
            DialogResult = _selection.HasValue;
            return;
        }

        _startScreen = snapped;
        _dragging = true;
        _selection = Drawing.Rectangle.Empty;
        InvalidateVisual();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        _currentScreen = Snap(ToScreenPoint(e.GetPosition(this)));

        if (_selectionMode == SelectionMode.FixedSize)
        {
            _selection = ClampToVirtualBounds(new Drawing.Rectangle(
                _currentScreen.X,
                _currentScreen.Y,
                _settings.FixedRegionWidth,
                _settings.FixedRegionHeight));
            InvalidateVisual();
            return;
        }

        if (_dragging)
        {
            _selection = Normalize(_startScreen, _currentScreen);
            InvalidateVisual();
        }
        else
        {
            InvalidateVisual();
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_selectionMode != SelectionMode.Freeform || !_dragging)
        {
            return;
        }

        _dragging = false;

        if (_selection.HasValue && _selection.Value.Width >= 2 && _selection.Value.Height >= 2)
        {
            SelectedRegion = ClampToVirtualBounds(_selection.Value);
            if (SelectedRegion.HasValue)
            {
                DialogResult = true;
            }
        }
        else
        {
            _selection = null;
            InvalidateVisual();
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }

    private Drawing.Point ToScreenPoint(Point localPoint)
    {
        return new Drawing.Point(
            _virtualBounds.Left + (int)Math.Round(localPoint.X),
            _virtualBounds.Top + (int)Math.Round(localPoint.Y));
    }

    private Drawing.Point Snap(Drawing.Point point)
    {
        return _edgeSnapService.SnapPoint(point, _snapTargets, threshold: 12);
    }

    private Drawing.Rectangle Normalize(Drawing.Point a, Drawing.Point b)
    {
        var left = Math.Min(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        var right = Math.Max(a.X, b.X);
        var bottom = Math.Max(a.Y, b.Y);
        return ClampToVirtualBounds(Drawing.Rectangle.FromLTRB(left, top, right, bottom)) ?? Drawing.Rectangle.Empty;
    }

    private Drawing.Rectangle? ClampToVirtualBounds(Drawing.Rectangle rect)
    {
        var left = Math.Max(rect.Left, _virtualBounds.Left);
        var top = Math.Max(rect.Top, _virtualBounds.Top);
        var right = Math.Min(rect.Right, _virtualBounds.Right);
        var bottom = Math.Min(rect.Bottom, _virtualBounds.Bottom);

        var width = right - left;
        var height = bottom - top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        return new Drawing.Rectangle(left, top, width, height);
    }

    private WpfRect ToLocalRect(Drawing.Rectangle screenRect)
    {
        return new WpfRect(
            screenRect.Left - _virtualBounds.Left,
            screenRect.Top - _virtualBounds.Top,
            screenRect.Width,
            screenRect.Height);
    }

    private void DrawUndimmedSelection(DrawingContext dc, Drawing.Rectangle screenRect, WpfRect localRect)
    {
        if (_background is null)
        {
            return;
        }

        var x = screenRect.Left - _virtualBounds.Left;
        var y = screenRect.Top - _virtualBounds.Top;
        var width = Math.Max(1, Math.Min(screenRect.Width, _background.PixelWidth - x));
        var height = Math.Max(1, Math.Min(screenRect.Height, _background.PixelHeight - y));
        var crop = new Int32Rect(
            x,
            y,
            width,
            height);

        var undimmed = new CroppedBitmap(_background, crop);
        undimmed.Freeze();
        dc.DrawImage(undimmed, localRect);
    }

    private void DrawSelectionLabel(DrawingContext dc, Drawing.Rectangle selected, WpfRect localRect)
    {
        var text = $"{selected.Width} x {selected.Height}";
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            14,
            Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        var labelRect = new WpfRect(localRect.X, Math.Max(0, localRect.Y - 28), formatted.Width + 12, 24);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), null, labelRect);
        dc.DrawText(formatted, new Point(labelRect.X + 6, labelRect.Y + 4));
    }

    private void DrawLoupe(DrawingContext dc)
    {
        if (_background is null)
        {
            return;
        }

        var loupeSize = 140.0;
        var sampleSize = 28;
        var localCursor = new Point(
            _currentScreen.X - _virtualBounds.Left,
            _currentScreen.Y - _virtualBounds.Top);

        var loupeX = localCursor.X + 28;
        var loupeY = localCursor.Y + 28;
        if (loupeX + loupeSize > ActualWidth)
        {
            loupeX = localCursor.X - loupeSize - 28;
        }

        if (loupeY + loupeSize > ActualHeight)
        {
            loupeY = localCursor.Y - loupeSize - 28;
        }

        loupeX = Math.Clamp(loupeX, 0, Math.Max(0, ActualWidth - loupeSize));
        loupeY = Math.Clamp(loupeY, 0, Math.Max(0, ActualHeight - loupeSize));

        var sampleLeft = Math.Clamp((int)localCursor.X - sampleSize / 2, 0, Math.Max(0, _background.PixelWidth - sampleSize));
        var sampleTop = Math.Clamp((int)localCursor.Y - sampleSize / 2, 0, Math.Max(0, _background.PixelHeight - sampleSize));
        var crop = new Int32Rect(sampleLeft, sampleTop, sampleSize, sampleSize);
        var cropped = new CroppedBitmap(_background, crop);
        cropped.Freeze();

        var loupeRect = new WpfRect(loupeX, loupeY, loupeSize, loupeSize);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(220, 20, 20, 20)), new Pen(Brushes.White, 1), loupeRect);
        dc.DrawImage(cropped, loupeRect);

        var centerX = loupeRect.Left + loupeRect.Width / 2;
        var centerY = loupeRect.Top + loupeRect.Height / 2;
        dc.DrawLine(new Pen(Brushes.Red, 1), new Point(centerX, loupeRect.Top), new Point(centerX, loupeRect.Bottom));
        dc.DrawLine(new Pen(Brushes.Red, 1), new Point(loupeRect.Left, centerY), new Point(loupeRect.Right, centerY));
    }
}
