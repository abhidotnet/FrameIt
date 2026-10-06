using System.Windows;
using FrameIt.Models;
using Forms = System.Windows.Forms;

namespace FrameIt.UI;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _workingCopy;

    public SettingsWindow(AppSettings source)
    {
        InitializeComponent();
        _workingCopy = Clone(source);
        Bind();
    }

    public AppSettings? UpdatedSettings { get; private set; }

    private void Bind()
    {
        CaptureFolderTextBox.Text = _workingCopy.CaptureFolder;
        FixedWidthTextBox.Text = _workingCopy.FixedRegionWidth.ToString();
        FixedHeightTextBox.Text = _workingCopy.FixedRegionHeight.ToString();
        TimingLogsCheckBox.IsChecked = _workingCopy.EnableTimingLogs;

        RegionHotkeyTextBox.Text = _workingCopy.Hotkeys[CaptureMode.Region];
        FullScreenHotkeyTextBox.Text = _workingCopy.Hotkeys[CaptureMode.FullScreen];
        ActiveWindowHotkeyTextBox.Text = _workingCopy.Hotkeys[CaptureMode.ActiveWindow];
        FixedRegionHotkeyTextBox.Text = _workingCopy.Hotkeys[CaptureMode.FixedRegion];
    }

    private void BrowseFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        using var picker = new Forms.FolderBrowserDialog
        {
            Description = "Select where FrameIt saves PNG captures.",
            SelectedPath = CaptureFolderTextBox.Text
        };

        if (picker.ShowDialog() == Forms.DialogResult.OK)
        {
            CaptureFolderTextBox.Text = picker.SelectedPath;
        }
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(FixedWidthTextBox.Text, out var width) || width < 32)
        {
            System.Windows.MessageBox.Show(this, "Fixed width must be a number >= 32.", "FrameIt", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(FixedHeightTextBox.Text, out var height) || height < 32)
        {
            System.Windows.MessageBox.Show(this, "Fixed height must be a number >= 32.", "FrameIt", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var hotkeys = new Dictionary<CaptureMode, string>
        {
            [CaptureMode.Region] = RegionHotkeyTextBox.Text.Trim(),
            [CaptureMode.FullScreen] = FullScreenHotkeyTextBox.Text.Trim(),
            [CaptureMode.ActiveWindow] = ActiveWindowHotkeyTextBox.Text.Trim(),
            [CaptureMode.FixedRegion] = FixedRegionHotkeyTextBox.Text.Trim()
        };

        foreach (var pair in hotkeys)
        {
            if (!HotkeyBinding.TryParse(pair.Value, out _, out var error))
            {
                System.Windows.MessageBox.Show(
                    this,
                    $"Hotkey for {pair.Key} is invalid: {error}",
                    "FrameIt",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        UpdatedSettings = new AppSettings
        {
            CaptureFolder = string.IsNullOrWhiteSpace(CaptureFolderTextBox.Text)
                ? _workingCopy.CaptureFolder
                : CaptureFolderTextBox.Text.Trim(),
            FixedRegionWidth = width,
            FixedRegionHeight = height,
            EnableTimingLogs = TimingLogsCheckBox.IsChecked == true,
            Hotkeys = hotkeys
        };

        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static AppSettings Clone(AppSettings source)
    {
        return new AppSettings
        {
            CaptureFolder = source.CaptureFolder,
            FixedRegionWidth = source.FixedRegionWidth,
            FixedRegionHeight = source.FixedRegionHeight,
            EnableTimingLogs = source.EnableTimingLogs,
            Hotkeys = source.Hotkeys.ToDictionary(pair => pair.Key, pair => pair.Value)
        };
    }
}
