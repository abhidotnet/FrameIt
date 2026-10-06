using System.Windows;
using FrameIt.Editing;

namespace FrameIt.UI;

public partial class ResizeWindow : Window
{
    private readonly int _originalWidth;
    private readonly int _originalHeight;
    private bool _updating;

    public ResizeWindow(int width, int height)
    {
        InitializeComponent();
        _originalWidth = Math.Max(1, width);
        _originalHeight = Math.Max(1, height);
        OriginalSizeText.Text = $"Current size: {_originalWidth} × {_originalHeight}";
        PixelWidthBox.Text = _originalWidth.ToString();
        PixelHeightBox.Text = _originalHeight.ToString();
        PercentWidthBox.Text = "100";
        PercentHeightBox.Text = "100";
        ResultWidth = _originalWidth;
        ResultHeight = _originalHeight;

        PixelWidthBox.TextChanged += (_, _) => OnPixelWidthChanged();
        PixelHeightBox.TextChanged += (_, _) => OnPixelHeightChanged();
        PercentWidthBox.TextChanged += (_, _) => OnPercentWidthChanged();
        PercentHeightBox.TextChanged += (_, _) => OnPercentHeightChanged();
        AspectLockCheckBox.Checked += (_, _) => ApplyAspectFromWidth();
        AspectLockCheckBox.Unchecked += (_, _) => { };
    }

    public int ResultWidth { get; private set; }

    public int ResultHeight { get; private set; }

    private bool AspectLocked => AspectLockCheckBox.IsChecked == true;

    private void OnPixelWidthChanged()
    {
        if (_updating || !int.TryParse(PixelWidthBox.Text, out var width))
        {
            return;
        }

        _updating = true;
        if (AspectLocked)
        {
            var height = ScaleFromWidth(width);
            PixelHeightBox.Text = height.ToString();
            PercentHeightBox.Text = PercentOf(height, _originalHeight).ToString();
        }

        PercentWidthBox.Text = PercentOf(width, _originalWidth).ToString();
        _updating = false;
    }

    private void OnPixelHeightChanged()
    {
        if (_updating || !int.TryParse(PixelHeightBox.Text, out var height))
        {
            return;
        }

        _updating = true;
        if (AspectLocked)
        {
            var width = ScaleFromHeight(height);
            PixelWidthBox.Text = width.ToString();
            PercentWidthBox.Text = PercentOf(width, _originalWidth).ToString();
        }

        PercentHeightBox.Text = PercentOf(height, _originalHeight).ToString();
        _updating = false;
    }

    private void OnPercentWidthChanged()
    {
        if (_updating || !int.TryParse(PercentWidthBox.Text, out var percent))
        {
            return;
        }

        _updating = true;
        var width = PixelsFromPercent(percent, _originalWidth);
        PixelWidthBox.Text = width.ToString();
        if (AspectLocked)
        {
            PercentHeightBox.Text = percent.ToString();
            PixelHeightBox.Text = ScaleFromWidth(width).ToString();
        }

        _updating = false;
    }

    private void OnPercentHeightChanged()
    {
        if (_updating || !int.TryParse(PercentHeightBox.Text, out var percent))
        {
            return;
        }

        _updating = true;
        var height = PixelsFromPercent(percent, _originalHeight);
        PixelHeightBox.Text = height.ToString();
        if (AspectLocked)
        {
            PercentWidthBox.Text = percent.ToString();
            PixelWidthBox.Text = ScaleFromHeight(height).ToString();
        }

        _updating = false;
    }

    private void ApplyAspectFromWidth()
    {
        if (int.TryParse(PixelWidthBox.Text, out var width))
        {
            OnPixelWidthChanged();
            if (!_updating)
            {
                _updating = true;
                var height = ScaleFromWidth(width);
                PixelHeightBox.Text = height.ToString();
                PercentHeightBox.Text = PercentOf(height, _originalHeight).ToString();
                _updating = false;
            }
        }
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PixelWidthBox.Text, out var width) ||
            !int.TryParse(PixelHeightBox.Text, out var height) ||
            width < 1 ||
            height < 1 ||
            width > ImageEffects.MaxDimension ||
            height > ImageEffects.MaxDimension)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Width and height must be whole pixels from 1 to {ImageEffects.MaxDimension}.",
                "FrameIt",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ResultWidth = width;
        ResultHeight = height;
        DialogResult = true;
    }

    private int ScaleFromWidth(int width)
    {
        return Math.Clamp((int)Math.Round(width * (_originalHeight / (double)_originalWidth)), 1, ImageEffects.MaxDimension);
    }

    private int ScaleFromHeight(int height)
    {
        return Math.Clamp((int)Math.Round(height * (_originalWidth / (double)_originalHeight)), 1, ImageEffects.MaxDimension);
    }

    private static int PercentOf(int pixels, int original)
    {
        return Math.Clamp((int)Math.Round(pixels * 100.0 / original), 1, 1000);
    }

    private static int PixelsFromPercent(int percent, int original)
    {
        return Math.Clamp((int)Math.Round(original * (percent / 100.0)), 1, ImageEffects.MaxDimension);
    }
}
