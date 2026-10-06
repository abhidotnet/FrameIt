using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using FrameIt.Editing;
using FrameIt.Models;
using FrameIt.Services;
using Forms = System.Windows.Forms;
using DrawingColor = System.Drawing.Color;

namespace FrameIt.UI;

public partial class EditorWindow : Window
{
    private static readonly DrawingColor[] Palette =
    {
        DrawingColor.FromArgb(229, 57, 53),
        DrawingColor.FromArgb(251, 140, 0),
        DrawingColor.FromArgb(253, 216, 53),
        DrawingColor.FromArgb(67, 160, 71),
        DrawingColor.FromArgb(30, 136, 229),
        DrawingColor.FromArgb(142, 36, 170),
        DrawingColor.White,
        DrawingColor.FromArgb(33, 33, 33)
    };

    private readonly EditorSession _session;
    private readonly Func<AppSettings> _getSettings;
    private readonly Action _persistSettings;
    private readonly List<System.Windows.Controls.Button> _colorButtons = new();

    private string? _filePath;
    private EditorTool _tool = EditorTool.Select;
    private DrawingColor _color = Palette[0];
    private float _strokeThickness = 3;
    private float _highlightThickness = 16;
    private float _fontSize = 18;
    private int _strength = 8;
    private Annotation? _selectedAnnotation;
    private Redaction? _selectedRedaction;
    private bool _dragging;
    private bool _moved;
    private PointD _dragStart;
    private PointD _dragLast;
    private List<Annotation>? _annotationSnapshot;
    private List<Redaction>? _redactionSnapshot;
    private List<PointD>? _livePoints;
    private List<Annotation>? _styleAnnotations;
    private List<Redaction>? _styleRedactions;
    private bool _styleDirty;
    private bool _sliderInternal;
    private bool _suppressTextCommit;
    private Annotation? _textEditTarget;
    private List<Annotation>? _textSnapshot;
    private double _textX;
    private double _textY;
    private Bitmap? _displayedBitmap;
    private Bitmap? _previewBitmap;
    private readonly Dictionary<Redaction, RedactionPatch> _patches = new();

    public EditorWindow(Bitmap bitmap, string? filePath, Func<AppSettings> getSettings, Action persistSettings)
    {
        InitializeComponent();
        _session = new EditorSession(bitmap, startDirty: string.IsNullOrEmpty(filePath));
        _filePath = string.IsNullOrEmpty(filePath) ? null : filePath;
        _getSettings = getSettings;
        _persistSettings = persistSettings;

        BuildPalette();
        JpegQualityTextBox.Text = Math.Clamp(_getSettings().JpegQuality, 1, 100).ToString();
        ThicknessSlider.ValueChanged += (_, _) => OnThicknessChanged();
        FontSlider.ValueChanged += (_, _) => OnFontSizeChanged();
        StrengthSlider.ValueChanged += (_, _) => OnStrengthChanged();
        ThicknessSlider.LostMouseCapture += (_, _) => CommitStyleChange();
        FontSlider.LostMouseCapture += (_, _) => CommitStyleChange();
        StrengthSlider.LostMouseCapture += (_, _) => CommitStyleChange();
        ThicknessSlider.KeyUp += (_, _) => CommitStyleChange();
        FontSlider.KeyUp += (_, _) => CommitStyleChange();
        StrengthSlider.KeyUp += (_, _) => CommitStyleChange();
        CanvasHost.PreviewMouseLeftButtonDown += CanvasHost_OnMouseDown;
        CanvasHost.PreviewMouseMove += CanvasHost_OnMouseMove;
        CanvasHost.PreviewMouseLeftButtonUp += CanvasHost_OnMouseUp;
        CanvasHost.LostMouseCapture += (_, _) => FinishDrag(null);

        _session.Changed += OnSessionChanged;
        Loaded += (_, _) =>
        {
            FitToWorkArea();
            Focus();
        };
        DisplaySessionImage();
        UpdateStatus();
        HighlightPalette();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (!JpegQualityTextBox.IsKeyboardFocusWithin)
        {
            JpegQualityTextBox.Text = Math.Clamp(_getSettings().JpegQuality, 1, 100).ToString();
        }
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;

        if (key == Key.Escape)
        {
            if (_dragging)
            {
                CancelInteraction();
            }
            else if (InlineTextBox.Visibility == Visibility.Visible)
            {
                CancelInlineText();
            }
            else
            {
                Close();
            }

            e.Handled = true;
            return;
        }

        if (e.OriginalSource is System.Windows.Controls.TextBox)
        {
            if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.S)
            {
                SaveAs();
                e.Handled = true;
            }
            else if (mods == ModifierKeys.Control && key == Key.S)
            {
                Save();
                e.Handled = true;
            }

            return;
        }

        if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.S)
        {
            SaveAs();
            e.Handled = true;
        }
        else if (mods == ModifierKeys.Control && key == Key.S)
        {
            Save();
            e.Handled = true;
        }
        else if (mods == ModifierKeys.Control && key == Key.Z)
        {
            Undo();
            e.Handled = true;
        }
        else if (mods == ModifierKeys.Control && key == Key.Y)
        {
            Redo();
            e.Handled = true;
        }
        else if (mods == ModifierKeys.Control && key == Key.C)
        {
            CopyFlattened();
            e.Handled = true;
        }
        else if (key is Key.Delete or Key.Back)
        {
            DeleteSelection();
            e.Handled = true;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        CommitStyleChange();
        CommitInlineText();
        if (!e.Cancel && _session.IsDirty)
        {
            var result = System.Windows.MessageBox.Show(
                this,
                "Save changes to this capture before closing?",
                "FrameIt",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);
            if (result is MessageBoxResult.Cancel or MessageBoxResult.None)
            {
                e.Cancel = true;
            }
            else if (result == MessageBoxResult.Yes && !Save())
            {
                e.Cancel = true;
            }
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _session.Changed -= OnSessionChanged;
        _previewBitmap?.Dispose();
        _previewBitmap = null;
        _patches.Clear();
        _session.Dispose();
        base.OnClosed(e);
    }

    private void BuildPalette()
    {
        foreach (var color in Palette)
        {
            var button = new System.Windows.Controls.Button
            {
                Width = 22,
                MinWidth = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 4, 4),
                Padding = new Thickness(0),
                Background = BrushFrom(color),
                BorderBrush = System.Windows.Media.Brushes.Gray,
                BorderThickness = new Thickness(1),
                ToolTip = "Annotation color",
                Tag = color
            };
            var chosen = color;
            button.Click += (_, _) => SetColor(chosen);
            _colorButtons.Add(button);
            ColorPalette.Children.Add(button);
        }
    }

    private void ToolButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string name || !Enum.TryParse(name, out EditorTool tool))
        {
            return;
        }

        SetTool(tool);
    }

    private void SetTool(EditorTool tool)
    {
        CommitStyleChange();
        CommitInlineText();
        _tool = tool;
        if (tool != EditorTool.Select)
        {
            _selectedAnnotation = null;
            _selectedRedaction = null;
        }

        foreach (var child in ToolsPanel.Children)
        {
            if (child is ToggleButton toggle && toggle.Tag is string tag && Enum.TryParse(tag, out EditorTool parsed))
            {
                toggle.IsChecked = parsed == tool;
            }
        }

        CanvasHost.Cursor = tool switch
        {
            EditorTool.Select => System.Windows.Input.Cursors.Arrow,
            EditorTool.Text => System.Windows.Input.Cursors.IBeam,
            _ => System.Windows.Input.Cursors.Cross
        };
        SyncSlidersFromSelection();
        RebuildVisuals();
        UpdateStatus();
    }

    private void CustomColor_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.ColorDialog
        {
            Color = _color,
            FullOpen = true
        };
        var owner = new Win32Window(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        if (dialog.ShowDialog(owner) == Forms.DialogResult.OK)
        {
            SetColor(dialog.Color);
        }
    }

    private void SetColor(DrawingColor color)
    {
        _color = DrawingColor.FromArgb(255, color.R, color.G, color.B);
        HighlightPalette();
        if (_selectedAnnotation is null)
        {
            return;
        }

        var snapshot = AnnotationTransforms.Clone(_session.Annotations);
        _selectedAnnotation.Color = _color;
        _session.CommitApplied(snapshot, null);
    }

    private void HighlightPalette()
    {
        foreach (var button in _colorButtons)
        {
            var matches = button.Tag is DrawingColor color && color.ToArgb() == _color.ToArgb();
            button.BorderBrush = matches ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.Gray;
            button.BorderThickness = new Thickness(matches ? 2 : 1);
        }
    }

    private void RotateLeft_OnClick(object sender, RoutedEventArgs e) => Rotate(clockwise: false);

    private void RotateRight_OnClick(object sender, RoutedEventArgs e) => Rotate(clockwise: true);

    private void FlipHorizontal_OnClick(object sender, RoutedEventArgs e) => Flip(horizontal: true);

    private void FlipVertical_OnClick(object sender, RoutedEventArgs e) => Flip(horizontal: false);

    private void Resize_OnClick(object sender, RoutedEventArgs e)
    {
        CommitStyleChange();
        CommitInlineText();
        var dialog = new ResizeWindow(_session.Image.Width, _session.Image.Height)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (dialog.ResultWidth == _session.Image.Width && dialog.ResultHeight == _session.Image.Height)
        {
            return;
        }

        var scaleX = dialog.ResultWidth / (double)_session.Image.Width;
        var scaleY = dialog.ResultHeight / (double)_session.Image.Height;
        var resized = ImageEffects.Resize(_session.Image, dialog.ResultWidth, dialog.ResultHeight);
        var annotations = AnnotationTransforms.Scale(_session.Annotations, scaleX, scaleY);
        var redactions = AnnotationTransforms.ScaleRedactions(_session.Redactions, scaleX, scaleY);
        _selectedAnnotation = null;
        _selectedRedaction = null;
        _session.Apply(resized, annotations, redactions);
    }

    private void Adjust_OnClick(object sender, RoutedEventArgs e)
    {
        CommitStyleChange();
        CommitInlineText();
        var dialog = new AdjustWindow(ShowAdjustPreview)
        {
            Owner = this
        };
        var accepted = dialog.ShowDialog() == true;
        var brightness = dialog.Brightness;
        var contrast = dialog.Contrast;
        ClearPreview();
        if (accepted && (brightness != 0 || contrast != 0))
        {
            var adjusted = ImageEffects.Adjust(_session.Image, brightness, contrast);
            _session.Apply(adjusted, null, null);
            return;
        }

        DisplaySessionImage();
    }

    private void Undo_OnClick(object sender, RoutedEventArgs e) => Undo();

    private void Redo_OnClick(object sender, RoutedEventArgs e) => Redo();

    private void Copy_OnClick(object sender, RoutedEventArgs e) => CopyFlattened();

    private void Save_OnClick(object sender, RoutedEventArgs e) => Save();

    private void SaveAs_OnClick(object sender, RoutedEventArgs e) => SaveAs();

    private void JpegQualityTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(JpegQualityTextBox.Text.Trim(), out var quality) && quality is >= 1 and <= 100)
        {
            var settings = _getSettings();
            if (settings.JpegQuality != quality)
            {
                settings.JpegQuality = quality;
                _persistSettings();
            }

            return;
        }

        JpegQualityTextBox.Text = Math.Clamp(_getSettings().JpegQuality, 1, 100).ToString();
    }

    private void InlineTextBox_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitInlineText();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelInlineText();
            e.Handled = true;
        }
    }

    private void InlineTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressTextCommit)
        {
            return;
        }

        CommitInlineText();
    }

    private void Undo()
    {
        CommitStyleChange();
        CancelInlineText();
        _session.Undo();
    }

    private void Redo()
    {
        CommitStyleChange();
        CancelInlineText();
        _session.Redo();
    }

    private void DeleteSelection()
    {
        if (_selectedAnnotation is not null)
        {
            var next = _session.Annotations
                .Where(item => !ReferenceEquals(item, _selectedAnnotation))
                .Select(item => item.Clone())
                .ToList();
            _selectedAnnotation = null;
            _session.Apply(null, next, null);
            return;
        }

        if (_selectedRedaction is null)
        {
            return;
        }

        var redactions = _session.Redactions
            .Where(item => !ReferenceEquals(item, _selectedRedaction))
            .Select(item => item.Clone())
            .ToList();
        _selectedRedaction = null;
        _session.Apply(null, null, redactions);
    }

    private bool Save()
    {
        CommitStyleChange();
        CommitInlineText();
        if (string.IsNullOrEmpty(_filePath))
        {
            return SaveAs();
        }

        return WriteFile(_filePath, updatePath: false);
    }

    private bool SaveAs()
    {
        CommitStyleChange();
        CommitInlineText();
        var settings = _getSettings();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save capture",
            Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = _filePath is not null
                ? System.IO.Path.GetFileName(_filePath)
                : $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.png"
        };

        var initialDirectory = Directory.Exists(settings.LastSaveFolder)
            ? settings.LastSaveFolder
            : settings.CaptureFolder;
        if (Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        if (_filePath is not null && IsJpegPath(_filePath))
        {
            dialog.FilterIndex = 2;
            dialog.DefaultExt = ".jpg";
        }
        else
        {
            dialog.FilterIndex = 1;
            dialog.DefaultExt = ".png";
        }

        if (dialog.ShowDialog(this) != true)
        {
            return false;
        }

        var path = EnsureExtension(dialog.FileName, dialog.FilterIndex);
        return WriteFile(path, updatePath: true);
    }

    private bool WriteFile(string path, bool updatePath)
    {
        if (!TryReadJpegQuality(out var quality))
        {
            return false;
        }

        try
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var flattened = ImageEffects.Flatten(_session.Image, _session.Redactions, _session.Annotations);
            ImageEffects.Save(flattened, path, quality);
            var settings = _getSettings();
            settings.JpegQuality = quality;
            if (!string.IsNullOrEmpty(directory))
            {
                settings.LastSaveFolder = directory;
            }

            if (updatePath)
            {
                _filePath = path;
            }

            _session.MarkClean();
            _persistSettings();
            UpdateStatus();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            System.Windows.MessageBox.Show(
                this,
                "Could not save the image.\n" + ex.Message,
                "FrameIt",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private void CopyFlattened()
    {
        CommitStyleChange();
        CommitInlineText();
        using var flattened = ImageEffects.Flatten(_session.Image, _session.Redactions, _session.Annotations);
        var source = BitmapInterop.ToBitmapSource(flattened);
        if (!BitmapInterop.TrySetClipboard(source))
        {
            System.Windows.MessageBox.Show(
                this,
                "Could not copy the image to the clipboard.",
                "FrameIt",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private bool TryReadJpegQuality(out int quality)
    {
        if (int.TryParse(JpegQualityTextBox.Text.Trim(), out quality) && quality is >= 1 and <= 100)
        {
            return true;
        }

        quality = 0;
        System.Windows.MessageBox.Show(
            this,
            "JPEG quality must be a number from 1 to 100.",
            "FrameIt",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    private void Rotate(bool clockwise)
    {
        CommitStyleChange();
        CommitInlineText();
        var width = _session.Image.Width;
        var height = _session.Image.Height;
        var rotated = ImageEffects.Rotate(_session.Image, clockwise);
        Func<double, double, (double X, double Y)> map = clockwise
            ? (x, y) => (height - y, x)
            : (x, y) => (y, width - x);
        _selectedAnnotation = null;
        _selectedRedaction = null;
        _session.Apply(
            rotated,
            AnnotationTransforms.MapAll(_session.Annotations, map),
            AnnotationTransforms.MapRedactions(_session.Redactions, map));
    }

    private void Flip(bool horizontal)
    {
        CommitStyleChange();
        CommitInlineText();
        var width = _session.Image.Width;
        var height = _session.Image.Height;
        var flipped = ImageEffects.Flip(_session.Image, horizontal);
        Func<double, double, (double X, double Y)> map = horizontal
            ? (x, y) => (width - x, y)
            : (x, y) => (x, height - y);
        _selectedAnnotation = null;
        _selectedRedaction = null;
        _session.Apply(
            flipped,
            AnnotationTransforms.MapAll(_session.Annotations, map),
            AnnotationTransforms.MapRedactions(_session.Redactions, map));
    }

    private void ShowAdjustPreview(int brightness, int contrast)
    {
        var preview = ImageEffects.Adjust(_session.Image, brightness, contrast);
        BaseImage.Source = BitmapInterop.ToBitmapSource(preview);
        _previewBitmap?.Dispose();
        _previewBitmap = preview;
        _displayedBitmap = null;
    }

    private void ClearPreview()
    {
        _previewBitmap?.Dispose();
        _previewBitmap = null;
    }

    private void CanvasHost_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (InlineTextBox.IsVisible && InlineTextBox.IsMouseOver)
        {
            return;
        }

        CommitStyleChange();
        CommitInlineText();
        Focus();
        var point = ToImage(e);

        if (_tool == EditorTool.Select && e.ClickCount >= 2)
        {
            var text = HitAnnotation(point);
            if (text is { Kind: AnnotationKind.Text })
            {
                _selectedAnnotation = text;
                _selectedRedaction = null;
                BeginTextInput(text);
                e.Handled = true;
                return;
            }
        }

        if (_tool == EditorTool.Text)
        {
            BeginTextInput(point);
            e.Handled = true;
            return;
        }

        if (_tool == EditorTool.Step)
        {
            PlaceStep(point);
            e.Handled = true;
            return;
        }

        if (_tool == EditorTool.Select)
        {
            var annotation = HitAnnotation(point);
            if (annotation is not null)
            {
                _selectedAnnotation = annotation;
                _selectedRedaction = null;
                _annotationSnapshot = AnnotationTransforms.Clone(_session.Annotations);
                _redactionSnapshot = null;
            }
            else
            {
                var redaction = HitRedaction(point);
                _selectedAnnotation = null;
                _selectedRedaction = redaction;
                _annotationSnapshot = null;
                _redactionSnapshot = redaction is null ? null : AnnotationTransforms.Clone(_session.Redactions);
            }

            SyncSlidersFromSelection();
            RebuildVisuals();
            UpdateStatus();
            if (_selectedAnnotation is null && _selectedRedaction is null)
            {
                return;
            }
        }
        else if (_tool is EditorTool.Pen or EditorTool.Highlighter)
        {
            _livePoints = new List<PointD> { point };
            UpdatePenPreview();
        }

        _dragging = true;
        _moved = false;
        _dragStart = point;
        _dragLast = point;
        Mouse.Capture(CanvasHost);
        e.Handled = true;
    }

    private void CanvasHost_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var point = ToImage(e);
        if (Math.Abs(point.X - _dragLast.X) < 0.01 && Math.Abs(point.Y - _dragLast.Y) < 0.01)
        {
            return;
        }

        _moved = true;
        if (_tool == EditorTool.Select)
        {
            var dx = point.X - _dragLast.X;
            var dy = point.Y - _dragLast.Y;
            _dragLast = point;
            if (_selectedAnnotation is not null)
            {
                _selectedAnnotation.Translate(dx, dy);
                RebuildAnnotationVisuals();
            }
            else if (_selectedRedaction is not null)
            {
                _selectedRedaction.Translate(dx, dy);
                MoveRedactionVisual(_selectedRedaction, dx, dy);
            }

            return;
        }

        _dragLast = point;
        if (_tool is EditorTool.Pen or EditorTool.Highlighter)
        {
            if (_livePoints is not null && AnnotationGeometry.Hypot(point.X - _livePoints[^1].X, point.Y - _livePoints[^1].Y) >= 1.5)
            {
                _livePoints.Add(point);
                UpdatePenPreview();
            }

            return;
        }

        if (_tool is EditorTool.Crop or EditorTool.Blur or EditorTool.Pixelate)
        {
            ShowRubberBand(_dragStart, point);
            return;
        }

        UpdateShapePreview();
    }

    private void CanvasHost_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        FinishDrag(ToImage(e));
        e.Handled = true;
    }

    private void FinishDrag(PointD? endPoint)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        if (endPoint.HasValue)
        {
            _dragLast = endPoint.Value;
        }

        if (Mouse.Captured == CanvasHost)
        {
            Mouse.Capture(null);
        }

        var moved = _moved;
        var annotationSnapshot = _annotationSnapshot;
        var redactionSnapshot = _redactionSnapshot;
        var livePoints = _livePoints;
        _moved = false;
        _annotationSnapshot = null;
        _redactionSnapshot = null;
        _livePoints = null;
        PreviewCanvas.Children.Clear();

        switch (_tool)
        {
            case EditorTool.Select:
                if (moved && _selectedAnnotation is not null && annotationSnapshot is not null)
                {
                    _session.CommitApplied(annotationSnapshot, null);
                }
                else if (moved && _selectedRedaction is not null && redactionSnapshot is not null)
                {
                    _session.CommitApplied(null, redactionSnapshot);
                }

                break;
            case EditorTool.Crop:
                ApplyCrop(_dragStart, _dragLast);
                break;
            case EditorTool.Blur:
                AddRedaction(_dragStart, _dragLast, pixelate: false);
                break;
            case EditorTool.Pixelate:
                AddRedaction(_dragStart, _dragLast, pixelate: true);
                break;
            case EditorTool.Pen:
            case EditorTool.Highlighter:
                if (livePoints is { Count: > 0 })
                {
                    AddAnnotation(new Annotation
                    {
                        Kind = _tool == EditorTool.Highlighter ? AnnotationKind.Highlighter : AnnotationKind.Pen,
                        Color = _color,
                        Thickness = _tool == EditorTool.Highlighter ? _highlightThickness : _strokeThickness,
                        FontSize = _fontSize,
                        Points = livePoints
                    });
                }

                break;
            default:
                var draft = BuildDraft(_dragStart, _dragLast);
                if (draft is not null)
                {
                    AddAnnotation(draft);
                }

                break;
        }
    }

    private void CancelInteraction()
    {
        var annotationSnapshot = _annotationSnapshot;
        var redactionSnapshot = _redactionSnapshot;
        var moved = _moved;
        _dragging = false;
        _moved = false;
        _annotationSnapshot = null;
        _redactionSnapshot = null;
        _livePoints = null;
        if (Mouse.Captured == CanvasHost)
        {
            Mouse.Capture(null);
        }

        PreviewCanvas.Children.Clear();
        if (moved)
        {
            _session.Restore(annotationSnapshot, redactionSnapshot);
            _selectedAnnotation = null;
            _selectedRedaction = null;
        }

        RebuildVisuals();
        UpdateStatus();
    }

    private void ApplyCrop(PointD start, PointD end)
    {
        var left = ClampPixel(Math.Min(start.X, end.X), _session.Image.Width);
        var top = ClampPixel(Math.Min(start.Y, end.Y), _session.Image.Height);
        var right = ClampPixel(Math.Max(start.X, end.X), _session.Image.Width);
        var bottom = ClampPixel(Math.Max(start.Y, end.Y), _session.Image.Height);
        if (right - left < 2 || bottom - top < 2)
        {
            return;
        }

        var rect = new System.Drawing.Rectangle(left, top, right - left, bottom - top);
        var cropped = ImageEffects.Crop(_session.Image, rect);
        var annotations = AnnotationTransforms.Crop(_session.Annotations, left, top, rect.Width, rect.Height);
        var redactions = AnnotationTransforms.CropRedactions(_session.Redactions, left, top, rect.Width, rect.Height);
        _selectedAnnotation = null;
        _selectedRedaction = null;
        _session.Apply(cropped, annotations, redactions);
    }

    private void AddRedaction(PointD start, PointD end, bool pixelate)
    {
        var box = AnnotationGeometry.NormalizeRect(start.X, start.Y, end.X, end.Y);
        if (box.Width < 2 || box.Height < 2)
        {
            return;
        }

        var redaction = new Redaction
        {
            X = box.X,
            Y = box.Y,
            Width = box.Width,
            Height = box.Height,
            Pixelate = pixelate,
            Strength = _strength
        };
        var next = AnnotationTransforms.Clone(_session.Redactions);
        next.Add(redaction);
        _selectedRedaction = redaction;
        _selectedAnnotation = null;
        _session.Apply(null, null, next);
    }

    private void AddAnnotation(Annotation annotation)
    {
        var next = AnnotationTransforms.Clone(_session.Annotations);
        next.Add(annotation);
        _selectedAnnotation = annotation;
        _selectedRedaction = null;
        _session.Apply(null, next, null);
    }

    private void PlaceStep(PointD point)
    {
        var number = 1;
        foreach (var annotation in _session.Annotations)
        {
            if (annotation.Kind == AnnotationKind.Step && annotation.StepNumber >= number)
            {
                number = annotation.StepNumber + 1;
            }
        }

        AddAnnotation(new Annotation
        {
            Kind = AnnotationKind.Step,
            Color = _color,
            Thickness = _strokeThickness,
            FontSize = _fontSize,
            X = point.X,
            Y = point.Y,
            StepNumber = number
        });
    }

    private void BeginTextInput(PointD point)
    {
        _textEditTarget = null;
        _textSnapshot = null;
        _textX = point.X;
        _textY = point.Y;
        ShowTextBox(string.Empty, _fontSize, _color, point.X, point.Y);
    }

    private void BeginTextInput(Annotation annotation)
    {
        _textEditTarget = annotation;
        _textSnapshot = AnnotationTransforms.Clone(_session.Annotations);
        _textX = annotation.X;
        _textY = annotation.Y;
        ShowTextBox(annotation.Text, annotation.FontSize, annotation.Color, annotation.X, annotation.Y);
    }

    private void ShowTextBox(string text, float fontSize, DrawingColor color, double x, double y)
    {
        InlineTextBox.Text = text;
        InlineTextBox.FontSize = fontSize;
        InlineTextBox.Foreground = BrushFrom(color);
        Canvas.SetLeft(InlineTextBox, x);
        Canvas.SetTop(InlineTextBox, y);
        InlineTextBox.Visibility = Visibility.Visible;
        InlineTextBox.Focus();
        InlineTextBox.SelectAll();
    }

    private void CommitInlineText()
    {
        if (InlineTextBox.Visibility != Visibility.Visible)
        {
            return;
        }

        var text = InlineTextBox.Text.Trim();
        var target = _textEditTarget;
        var snapshot = _textSnapshot;
        var x = _textX;
        var y = _textY;
        HideInlineText();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (target is null)
        {
            var measured = ImageEffects.MeasureText(text, _fontSize);
            AddAnnotation(new Annotation
            {
                Kind = AnnotationKind.Text,
                Color = _color,
                Thickness = _strokeThickness,
                FontSize = _fontSize,
                X = x,
                Y = y,
                Text = text,
                TextWidth = measured.Width,
                TextHeight = measured.Height
            });
            return;
        }

        if (string.Equals(target.Text, text, StringComparison.Ordinal))
        {
            return;
        }

        var size = ImageEffects.MeasureText(text, target.FontSize);
        target.Text = text;
        target.TextWidth = size.Width;
        target.TextHeight = size.Height;
        if (snapshot is not null)
        {
            _session.CommitApplied(snapshot, null);
        }
    }

    private void CancelInlineText()
    {
        _suppressTextCommit = true;
        HideInlineText();
        _suppressTextCommit = false;
    }

    private void HideInlineText()
    {
        _textEditTarget = null;
        _textSnapshot = null;
        InlineTextBox.Visibility = Visibility.Collapsed;
        InlineTextBox.Text = string.Empty;
    }

    private void OnThicknessChanged()
    {
        if (_sliderInternal)
        {
            return;
        }

        var value = (float)ThicknessSlider.Value;
        ThicknessValue.Text = Math.Round(value).ToString();
        if (_selectedAnnotation is not null && Annotation.UsesThickness(_selectedAnnotation.Kind))
        {
            _styleAnnotations ??= AnnotationTransforms.Clone(_session.Annotations);
            _selectedAnnotation.Thickness = value;
            _styleDirty = true;
            RebuildAnnotationVisuals();
            return;
        }

        if (_tool == EditorTool.Highlighter)
        {
            _highlightThickness = value;
        }
        else
        {
            _strokeThickness = value;
        }
    }

    private void OnFontSizeChanged()
    {
        if (_sliderInternal)
        {
            return;
        }

        var value = (float)FontSlider.Value;
        FontValue.Text = Math.Round(value).ToString();
        if (_selectedAnnotation is not null && Annotation.UsesFontSize(_selectedAnnotation.Kind))
        {
            _styleAnnotations ??= AnnotationTransforms.Clone(_session.Annotations);
            _selectedAnnotation.FontSize = value;
            if (_selectedAnnotation.Kind == AnnotationKind.Text)
            {
                var measured = ImageEffects.MeasureText(_selectedAnnotation.Text, value);
                _selectedAnnotation.TextWidth = measured.Width;
                _selectedAnnotation.TextHeight = measured.Height;
            }

            _styleDirty = true;
            RebuildAnnotationVisuals();
            return;
        }

        _fontSize = value;
    }

    private void OnStrengthChanged()
    {
        if (_sliderInternal)
        {
            return;
        }

        var value = (int)Math.Round(StrengthSlider.Value);
        StrengthValue.Text = value.ToString();
        if (_selectedRedaction is null)
        {
            _strength = value;
            return;
        }

        _styleRedactions ??= AnnotationTransforms.Clone(_session.Redactions);
        _selectedRedaction.Strength = value;
        _styleDirty = true;
        RebuildRedactionVisuals();
    }

    private void CommitStyleChange()
    {
        if (!_styleDirty)
        {
            _styleAnnotations = null;
            _styleRedactions = null;
            return;
        }

        var annotations = _styleAnnotations;
        var redactions = _styleRedactions;
        _styleAnnotations = null;
        _styleRedactions = null;
        _styleDirty = false;
        _session.CommitApplied(annotations, redactions);
    }

    private void SyncSlidersFromSelection()
    {
        _sliderInternal = true;
        if (_selectedAnnotation is not null)
        {
            ThicknessSlider.Value = _selectedAnnotation.Thickness;
            FontSlider.Value = _selectedAnnotation.FontSize;
        }
        else if (_selectedRedaction is not null)
        {
            StrengthSlider.Value = _selectedRedaction.Strength;
        }
        else
        {
            ThicknessSlider.Value = _tool == EditorTool.Highlighter ? _highlightThickness : _strokeThickness;
            FontSlider.Value = _fontSize;
            StrengthSlider.Value = _strength;
        }

        ThicknessValue.Text = Math.Round(ThicknessSlider.Value).ToString();
        FontValue.Text = Math.Round(FontSlider.Value).ToString();
        StrengthValue.Text = Math.Round(StrengthSlider.Value).ToString();
        _sliderInternal = false;
    }

    private void OnSessionChanged()
    {
        if (_selectedAnnotation is not null && !_session.Annotations.Contains(_selectedAnnotation))
        {
            _selectedAnnotation = null;
        }

        if (_selectedRedaction is not null && !_session.Redactions.Contains(_selectedRedaction))
        {
            _selectedRedaction = null;
        }

        if (_previewBitmap is null && !ReferenceEquals(_displayedBitmap, _session.Image))
        {
            DisplaySessionImage();
        }

        RebuildVisuals();
        SyncSlidersFromSelection();
        UpdateStatus();
    }

    private void DisplaySessionImage()
    {
        BaseImage.Source = BitmapInterop.ToBitmapSource(_session.Image);
        _displayedBitmap = _session.Image;
        ClearPreview();
    }

    private void RebuildVisuals()
    {
        RebuildRedactionVisuals();
        RebuildAnnotationVisuals();
    }

    private void RebuildAnnotationVisuals()
    {
        ShapeCanvas.Children.Clear();
        foreach (var annotation in _session.Annotations)
        {
            var visual = CreateAnnotationElement(annotation);
            if (visual is null)
            {
                continue;
            }

            ShapeCanvas.Children.Add(visual);
            if (ReferenceEquals(annotation, _selectedAnnotation))
            {
                annotation.GetBounds(out var left, out var top, out var right, out var bottom);
                ShapeCanvas.Children.Add(CreateAdornerRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top)));
            }
        }
    }

    private void RebuildRedactionVisuals()
    {
        PrunePatches();
        RedactionCanvas.Children.Clear();
        foreach (var redaction in _session.Redactions)
        {
            var rect = redaction.ToPixelRect(_session.Image.Width, _session.Image.Height);
            if (rect.Width < 1 || rect.Height < 1)
            {
                continue;
            }

            var host = new Canvas
            {
                Width = rect.Width,
                Height = rect.Height,
                Tag = redaction,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(host, rect.X);
            Canvas.SetTop(host, rect.Y);
            host.Children.Add(new System.Windows.Controls.Image
            {
                Source = GetRedactionPatch(redaction, rect),
                Width = rect.Width,
                Height = rect.Height,
                SnapsToDevicePixels = true,
                IsHitTestVisible = false
            });
            if (ReferenceEquals(redaction, _selectedRedaction))
            {
                host.Children.Add(CreateAdornerRect(-2, -2, rect.Width + 4, rect.Height + 4));
            }

            RedactionCanvas.Children.Add(host);
        }
    }

    private BitmapSource GetRedactionPatch(Redaction redaction, System.Drawing.Rectangle rect)
    {
        if (_patches.TryGetValue(redaction, out var cached) &&
            ReferenceEquals(cached.Image, _session.Image) &&
            cached.X == rect.X &&
            cached.Y == rect.Y &&
            cached.Width == rect.Width &&
            cached.Height == rect.Height &&
            cached.Strength == redaction.Strength &&
            cached.Pixelate == redaction.Pixelate)
        {
            return cached.Source;
        }

        using var patch = ImageEffects.CreatePatch(_session.Image, rect, redaction.Pixelate, redaction.Strength);
        var source = BitmapInterop.ToBitmapSource(patch);
        _patches[redaction] = new RedactionPatch(source, _session.Image, rect.X, rect.Y, rect.Width, rect.Height, redaction.Strength, redaction.Pixelate);
        return source;
    }

    private void PrunePatches()
    {
        if (_patches.Count == 0)
        {
            return;
        }

        List<Redaction>? stale = null;
        foreach (var pair in _patches)
        {
            if (!_session.Redactions.Contains(pair.Key) || !ReferenceEquals(pair.Value.Image, _session.Image))
            {
                stale ??= new List<Redaction>();
                stale.Add(pair.Key);
            }
        }

        if (stale is null)
        {
            return;
        }

        foreach (var key in stale)
        {
            _patches.Remove(key);
        }
    }

    private void MoveRedactionVisual(Redaction redaction, double dx, double dy)
    {
        foreach (UIElement child in RedactionCanvas.Children)
        {
            if (child is FrameworkElement element && ReferenceEquals(element.Tag, redaction))
            {
                Canvas.SetLeft(element, Canvas.GetLeft(element) + dx);
                Canvas.SetTop(element, Canvas.GetTop(element) + dy);
            }
        }
    }

    private void UpdatePenPreview()
    {
        PreviewCanvas.Children.Clear();
        if (_livePoints is null || _livePoints.Count == 0)
        {
            return;
        }

        var preview = CreateAnnotationElement(new Annotation
        {
            Kind = _tool == EditorTool.Highlighter ? AnnotationKind.Highlighter : AnnotationKind.Pen,
            Color = _color,
            Thickness = _tool == EditorTool.Highlighter ? _highlightThickness : _strokeThickness,
            Points = _livePoints
        });
        if (preview is not null)
        {
            PreviewCanvas.Children.Add(preview);
        }
    }

    private void UpdateShapePreview()
    {
        PreviewCanvas.Children.Clear();
        var draft = BuildDraft(_dragStart, _dragLast);
        if (draft is null)
        {
            return;
        }

        var preview = CreateAnnotationElement(draft);
        if (preview is not null)
        {
            PreviewCanvas.Children.Add(preview);
        }
    }

    private void ShowRubberBand(PointD start, PointD end)
    {
        PreviewCanvas.Children.Clear();
        var box = AnnotationGeometry.NormalizeRect(start.X, start.Y, end.X, end.Y);
        if (box.Width < 1 || box.Height < 1)
        {
            return;
        }

        PreviewCanvas.Children.Add(CreateAdornerRect(box.X, box.Y, box.Width, box.Height));
    }

    private Annotation? BuildDraft(PointD start, PointD end)
    {
        var thickness = _tool == EditorTool.Highlighter ? _highlightThickness : _strokeThickness;
        switch (_tool)
        {
            case EditorTool.Arrow:
            case EditorTool.Line:
                if (AnnotationGeometry.Hypot(end.X - start.X, end.Y - start.Y) < 2)
                {
                    return null;
                }

                return new Annotation
                {
                    Kind = _tool == EditorTool.Arrow ? AnnotationKind.Arrow : AnnotationKind.Line,
                    Color = _color,
                    Thickness = thickness,
                    FontSize = _fontSize,
                    X = start.X,
                    Y = start.Y,
                    X2 = end.X,
                    Y2 = end.Y
                };
            case EditorTool.Rectangle:
            case EditorTool.Ellipse:
                var box = AnnotationGeometry.NormalizeRect(start.X, start.Y, end.X, end.Y);
                if (box.Width < 2 || box.Height < 2)
                {
                    return null;
                }

                return new Annotation
                {
                    Kind = _tool == EditorTool.Rectangle ? AnnotationKind.Rectangle : AnnotationKind.Ellipse,
                    Color = _color,
                    Thickness = thickness,
                    FontSize = _fontSize,
                    X = box.X,
                    Y = box.Y,
                    Width = box.Width,
                    Height = box.Height
                };
            default:
                return null;
        }
    }

    private UIElement? CreateAnnotationElement(Annotation annotation)
    {
        switch (annotation.Kind)
        {
            case AnnotationKind.Arrow:
                return CreateArrowElement(annotation);
            case AnnotationKind.Line:
                return new Line
                {
                    X1 = annotation.X,
                    Y1 = annotation.Y,
                    X2 = annotation.X2,
                    Y2 = annotation.Y2,
                    Stroke = BrushFrom(annotation.Color),
                    StrokeThickness = Math.Max(1, annotation.Thickness),
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    IsHitTestVisible = false
                };
            case AnnotationKind.Rectangle:
                return Place(new System.Windows.Shapes.Rectangle
                {
                    Width = Math.Max(1, annotation.Width),
                    Height = Math.Max(1, annotation.Height),
                    Stroke = BrushFrom(annotation.Color),
                    StrokeThickness = Math.Max(1, annotation.Thickness),
                    Fill = System.Windows.Media.Brushes.Transparent,
                    IsHitTestVisible = false
                }, annotation.X, annotation.Y);
            case AnnotationKind.Ellipse:
                return Place(new Ellipse
                {
                    Width = Math.Max(1, annotation.Width),
                    Height = Math.Max(1, annotation.Height),
                    Stroke = BrushFrom(annotation.Color),
                    StrokeThickness = Math.Max(1, annotation.Thickness),
                    Fill = System.Windows.Media.Brushes.Transparent,
                    IsHitTestVisible = false
                }, annotation.X, annotation.Y);
            case AnnotationKind.Pen:
            case AnnotationKind.Highlighter:
                return CreateStrokeElement(annotation);
            case AnnotationKind.Text:
                return Place(new TextBlock
                {
                    Text = annotation.Text,
                    Foreground = BrushFrom(annotation.Color),
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    FontSize = Math.Max(8, annotation.FontSize),
                    IsHitTestVisible = false
                }, annotation.X, annotation.Y);
            case AnnotationKind.Step:
                return CreateStepElement(annotation);
            default:
                return null;
        }
    }

    private static UIElement CreateArrowElement(Annotation annotation)
    {
        var head = AnnotationGeometry.ArrowHead(annotation.X, annotation.Y, annotation.X2, annotation.Y2, annotation.Thickness);
        var brush = BrushFrom(annotation.Color);
        var canvas = new Canvas { IsHitTestVisible = false };
        canvas.Children.Add(new Line
        {
            X1 = annotation.X,
            Y1 = annotation.Y,
            X2 = head.LineEnd.X,
            Y2 = head.LineEnd.Y,
            Stroke = brush,
            StrokeThickness = Math.Max(1, annotation.Thickness),
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Flat,
            IsHitTestVisible = false
        });
        canvas.Children.Add(new Polygon
        {
            Points = new PointCollection
            {
                new System.Windows.Point(head.Tip.X, head.Tip.Y),
                new System.Windows.Point(head.Left.X, head.Left.Y),
                new System.Windows.Point(head.Right.X, head.Right.Y)
            },
            Fill = brush,
            IsHitTestVisible = false
        });
        return canvas;
    }

    private static UIElement CreateStrokeElement(Annotation annotation)
    {
        var brush = annotation.Kind == AnnotationKind.Highlighter
            ? BrushFrom(annotation.Color, 96)
            : BrushFrom(annotation.Color);
        if (annotation.Points.Count == 1)
        {
            var radius = Math.Max(1, annotation.Thickness / 2);
            return Place(new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = brush,
                IsHitTestVisible = false
            }, annotation.Points[0].X - radius, annotation.Points[0].Y - radius);
        }

        return new Polyline
        {
            Points = new PointCollection(annotation.Points.Select(point => new System.Windows.Point(point.X, point.Y))),
            Stroke = brush,
            StrokeThickness = Math.Max(1, annotation.Thickness),
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false
        };
    }

    private static UIElement CreateStepElement(Annotation annotation)
    {
        var radius = Annotation.StepRadius(annotation.FontSize);
        var host = new Grid
        {
            Width = radius * 2,
            Height = radius * 2,
            IsHitTestVisible = false
        };
        host.Children.Add(new Ellipse
        {
            Fill = BrushFrom(annotation.Color),
            IsHitTestVisible = false
        });
        host.Children.Add(new TextBlock
        {
            Text = annotation.StepNumber.ToString(),
            Foreground = new SolidColorBrush(ContrastMedia(annotation.Color)),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontWeight = FontWeights.Bold,
            FontSize = Math.Max(8, annotation.FontSize * 0.75),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        });
        return Place(host, annotation.X - radius, annotation.Y - radius);
    }

    private static System.Windows.Shapes.Rectangle CreateAdornerRect(double x, double y, double width, double height)
    {
        return Place(new System.Windows.Shapes.Rectangle
        {
            Width = Math.Max(1, width),
            Height = Math.Max(1, height),
            Stroke = System.Windows.Media.Brushes.DeepSkyBlue,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 2 },
            Fill = System.Windows.Media.Brushes.Transparent,
            IsHitTestVisible = false
        }, x, y);
    }

    private static T Place<T>(T element, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        return element;
    }

    private static SolidColorBrush BrushFrom(DrawingColor color, byte? alpha = null)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha ?? color.A, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static System.Windows.Media.Color ContrastMedia(DrawingColor color)
    {
        var luminance = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
        return luminance > 160 ? Colors.Black : Colors.White;
    }

    private Annotation? HitAnnotation(PointD point)
    {
        for (var index = _session.Annotations.Count - 1; index >= 0; index--)
        {
            if (_session.Annotations[index].HitTest(point.X, point.Y))
            {
                return _session.Annotations[index];
            }
        }

        return null;
    }

    private Redaction? HitRedaction(PointD point)
    {
        for (var index = _session.Redactions.Count - 1; index >= 0; index--)
        {
            if (_session.Redactions[index].HitTest(point.X, point.Y))
            {
                return _session.Redactions[index];
            }
        }

        return null;
    }

    private PointD ToImage(System.Windows.Input.MouseEventArgs e)
    {
        var point = e.GetPosition(CanvasHost);
        var width = CanvasHost.ActualWidth;
        var height = CanvasHost.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return new PointD(0, 0);
        }

        var x = point.X * _session.Image.Width / width;
        var y = point.Y * _session.Image.Height / height;
        return new PointD(
            Math.Clamp(x, 0, _session.Image.Width),
            Math.Clamp(y, 0, _session.Image.Height));
    }

    private void UpdateStatus()
    {
        var dirty = _session.IsDirty ? "Unsaved changes" : "Saved";
        var path = _filePath ?? "Not saved yet";
        var hint = _tool switch
        {
            EditorTool.Select => "Drag to move. Delete removes the selection.",
            EditorTool.Crop => "Drag a rectangle to crop.",
            EditorTool.Text => "Click to place text.",
            EditorTool.Step => "Click to place the next numbered step.",
            EditorTool.Blur => "Drag a rectangle to blur.",
            EditorTool.Pixelate => "Drag a rectangle to pixelate.",
            _ => "Drag to draw."
        };
        StatusText.Text = $"{_session.Image.Width}×{_session.Image.Height}    {dirty}    {path}    {hint}";
        Title = _filePath is null ? "FrameIt Editor" : "FrameIt Editor — " + System.IO.Path.GetFileName(_filePath);
        UndoButton.IsEnabled = _session.CanUndo;
        RedoButton.IsEnabled = _session.CanRedo;
    }

    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Clamp(_session.Image.Width + 64, MinWidth, Math.Max(MinWidth, area.Width * 0.92));
        Height = Math.Clamp(_session.Image.Height + 240, MinHeight, Math.Max(MinHeight, area.Height * 0.92));
    }

    private static int ClampPixel(double value, int limit)
    {
        return (int)Math.Clamp(Math.Round(value), 0, limit);
    }

    private static string EnsureExtension(string path, int filterIndex)
    {
        if (!string.IsNullOrEmpty(System.IO.Path.GetExtension(path)))
        {
            return path;
        }

        return path + (filterIndex == 2 ? ".jpg" : ".png");
    }

    private static bool IsJpegPath(string path)
    {
        var extension = System.IO.Path.GetExtension(path);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RedactionPatch
    {
        public RedactionPatch(BitmapSource source, Bitmap image, int x, int y, int width, int height, int strength, bool pixelate)
        {
            Source = source;
            Image = image;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Strength = strength;
            Pixelate = pixelate;
        }

        public BitmapSource Source { get; }

        public Bitmap Image { get; }

        public int X { get; }

        public int Y { get; }

        public int Width { get; }

        public int Height { get; }

        public int Strength { get; }

        public bool Pixelate { get; }
    }

    private sealed class Win32Window : Forms.IWin32Window
    {
        public Win32Window(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }
    }
}
