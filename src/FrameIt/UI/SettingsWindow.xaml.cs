using System.IO;
using System.Windows;
using System.Windows.Controls;
using FrameIt.Models;
using FrameIt.Services;
using FrameIt.Services.Sharing;
using Forms = System.Windows.Forms;

namespace FrameIt.UI;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _workingCopy;
    private readonly WindowsCredentialStore _credentials = new();

    public SettingsWindow(AppSettings source)
    {
        InitializeComponent();
        _workingCopy = Clone(source);
        SmtpSecurityCombo.ItemsSource = new[]
        {
            new Choice(SmtpSecurityMode.None, "None"),
            new Choice(SmtpSecurityMode.StartTls, "STARTTLS")
        };
        SmtpSecurityCombo.DisplayMemberPath = nameof(Choice.Label);

        SftpAuthCombo.ItemsSource = new[]
        {
            new Choice(SftpAuthMode.Password, "Password"),
            new Choice(SftpAuthMode.PrivateKey, "Private key")
        };
        SftpAuthCombo.DisplayMemberPath = nameof(Choice.Label);

        SmtpSecurityNote.Text = SmtpMailSender.SecurityNote;
        SftpNoticeText.Text = UnsupportedSftpUploader.Explanation;
        Bind();
    }

    public AppSettings? UpdatedSettings { get; private set; }

    private void Bind()
    {
        CaptureFolderTextBox.Text = _workingCopy.CaptureFolder;
        FixedWidthTextBox.Text = _workingCopy.FixedRegionWidth.ToString();
        FixedHeightTextBox.Text = _workingCopy.FixedRegionHeight.ToString();
        CaptureDelayTextBox.Text = _workingCopy.CaptureDelaySeconds.ToString();
        TimingLogsCheckBox.IsChecked = _workingCopy.EnableTimingLogs;
        AutoSaveCheckBox.IsChecked = _workingCopy.AutoSaveCaptures;
        JpegQualityTextBox.Text = _workingCopy.JpegQuality.ToString();

        RegionHotkeyTextBox.Text = _workingCopy.Hotkeys[CaptureMode.Region];
        FullScreenHotkeyTextBox.Text = _workingCopy.Hotkeys[CaptureMode.FullScreen];
        ActiveWindowHotkeyTextBox.Text = _workingCopy.Hotkeys[CaptureMode.ActiveWindow];
        FixedRegionHotkeyTextBox.Text = _workingCopy.Hotkeys[CaptureMode.FixedRegion];

        SmtpHostTextBox.Text = _workingCopy.Smtp.Host;
        SmtpPortTextBox.Text = _workingCopy.Smtp.Port.ToString();
        SelectChoice(SmtpSecurityCombo, _workingCopy.Smtp.Security);

        SmtpUsernameTextBox.Text = _workingCopy.Smtp.Username;
        SmtpFromTextBox.Text = _workingCopy.Smtp.FromAddress;
        SmtpToTextBox.Text = _workingCopy.Smtp.DefaultToAddress;

        FtpHostTextBox.Text = _workingCopy.Ftp.Host;
        FtpPortTextBox.Text = _workingCopy.Ftp.Port.ToString();
        FtpPathTextBox.Text = _workingCopy.Ftp.RemotePath;
        FtpUsernameTextBox.Text = _workingCopy.Ftp.Username;

        SftpHostTextBox.Text = _workingCopy.Sftp.Host;
        SftpPortTextBox.Text = _workingCopy.Sftp.Port.ToString();
        SftpPathTextBox.Text = _workingCopy.Sftp.RemotePath;
        SftpUsernameTextBox.Text = _workingCopy.Sftp.Username;
        SelectChoice(SftpAuthCombo, _workingCopy.Sftp.AuthMode);

        SftpKeyPathTextBox.Text = _workingCopy.Sftp.PrivateKeyPath;

        SmtpStoredText.Text = DescribeStored("password", CredentialNames.SmtpPassword);
        FtpStoredText.Text = DescribeStored("password", CredentialNames.FtpPassword);
        SftpPasswordStoredText.Text = DescribeStored("password", CredentialNames.SftpPassword);
        SftpPassphraseStoredText.Text = DescribeStored("passphrase", CredentialNames.SftpPassphrase);
        UpdateSftpPanels();
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

    private void SftpBrowseKeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select the SFTP private key",
            Filter = "Key files (*.pem;*.key)|*.pem;*.key|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (File.Exists(SftpKeyPathTextBox.Text))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(SftpKeyPathTextBox.Text);
        }

        if (dialog.ShowDialog(this) == true)
        {
            SftpKeyPathTextBox.Text = dialog.FileName;
        }
    }

    private void SftpAuthCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded && SftpPasswordPanel is null)
        {
            return;
        }

        UpdateSftpPanels();
    }

    private void UpdateSftpPanels()
    {
        if (SftpPasswordPanel is null || SftpKeyPanel is null)
        {
            return;
        }

        var useKey = SelectedChoice(SftpAuthCombo, SftpAuthMode.Password) == SftpAuthMode.PrivateKey;
        SftpPasswordPanel.Visibility = useKey ? Visibility.Collapsed : Visibility.Visible;
        SftpKeyPanel.Visibility = useKey ? Visibility.Visible : Visibility.Collapsed;
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

        if (!int.TryParse(CaptureDelayTextBox.Text, out var delay) || delay is < 0 or > 10)
        {
            System.Windows.MessageBox.Show(this, "Capture delay must be a whole number from 0 to 10.", "FrameIt", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(JpegQualityTextBox.Text, out var jpegQuality) || jpegQuality is < 1 or > 100)
        {
            System.Windows.MessageBox.Show(this, "JPEG quality must be a number from 1 to 100.", "FrameIt", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryParsePort(SmtpPortTextBox.Text, "SMTP", out var smtpPort) ||
            !TryParsePort(FtpPortTextBox.Text, "FTP", out var ftpPort) ||
            !TryParsePort(SftpPortTextBox.Text, "SFTP", out var sftpPort))
        {
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

        var smtp = new SmtpSettings
        {
            Host = SmtpHostTextBox.Text.Trim(),
            Port = smtpPort,
            Security = SelectedChoice(SmtpSecurityCombo, SmtpSecurityMode.StartTls),
            Username = SmtpUsernameTextBox.Text.Trim(),
            FromAddress = SmtpFromTextBox.Text.Trim(),
            DefaultToAddress = SmtpToTextBox.Text.Trim()
        };
        var ftp = new FtpSettings
        {
            Host = FtpHostTextBox.Text.Trim(),
            Port = ftpPort,
            RemotePath = FtpPathTextBox.Text.Trim(),
            Username = FtpUsernameTextBox.Text.Trim()
        };
        var sftp = new SftpSettings
        {
            Host = SftpHostTextBox.Text.Trim(),
            Port = sftpPort,
            RemotePath = SftpPathTextBox.Text.Trim(),
            Username = SftpUsernameTextBox.Text.Trim(),
            AuthMode = SelectedChoice(SftpAuthCombo, SftpAuthMode.Password),
            PrivateKeyPath = SftpKeyPathTextBox.Text.Trim()
        };

        if (!smtp.IsBlank && smtp.GetConfigurationError(requireRecipient: false) is { } smtpError)
        {
            System.Windows.MessageBox.Show(this, smtpError, "FrameIt", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!ftp.IsBlank && ftp.GetConfigurationError() is { } ftpError)
        {
            System.Windows.MessageBox.Show(this, ftpError, "FrameIt", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!sftp.IsBlank && sftp.GetConfigurationError() is { } sftpError)
        {
            System.Windows.MessageBox.Show(this, sftpError, "FrameIt", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var smtpUser = string.IsNullOrWhiteSpace(smtp.Username) ? smtp.FromAddress : smtp.Username;
        if (!TryApplySecret(SmtpPasswordBox, SmtpRemoveCheckBox, CredentialNames.SmtpPassword, smtpUser, "FrameIt SMTP password") ||
            !TryApplySecret(FtpPasswordBox, FtpRemoveCheckBox, CredentialNames.FtpPassword, ftp.Username, "FrameIt FTP password") ||
            !TryApplySecret(SftpPasswordBox, SftpRemovePasswordCheckBox, CredentialNames.SftpPassword, sftp.Username, "FrameIt SFTP password") ||
            !TryApplySecret(SftpPassphraseBox, SftpRemovePassphraseCheckBox, CredentialNames.SftpPassphrase, sftp.Username, "FrameIt SFTP private key passphrase"))
        {
            return;
        }

        UpdatedSettings = new AppSettings
        {
            CaptureFolder = string.IsNullOrWhiteSpace(CaptureFolderTextBox.Text)
                ? _workingCopy.CaptureFolder
                : CaptureFolderTextBox.Text.Trim(),
            FixedRegionWidth = width,
            FixedRegionHeight = height,
            CaptureDelaySeconds = delay,
            EnableTimingLogs = TimingLogsCheckBox.IsChecked == true,
            AutoSaveCaptures = AutoSaveCheckBox.IsChecked == true,
            JpegQuality = jpegQuality,
            LastSaveFolder = _workingCopy.LastSaveFolder,
            Smtp = smtp,
            Ftp = ftp,
            Sftp = sftp,
            Hotkeys = hotkeys
        };

        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private bool TryParsePort(string text, string label, out int port)
    {
        if (int.TryParse(text, out port) && port is >= 1 and <= 65535)
        {
            return true;
        }

        port = 0;
        System.Windows.MessageBox.Show(
            this,
            label + " port must be a number from 1 to 65535.",
            "FrameIt",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    private bool TryApplySecret(PasswordBox box, System.Windows.Controls.CheckBox remove, string target, string userName, string comment)
    {
        try
        {
            if (!string.IsNullOrEmpty(box.Password))
            {
                _credentials.Write(target, userName, box.Password, comment);
                return true;
            }

            if (remove.IsChecked == true)
            {
                _credentials.Delete(target);
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "FrameIt", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private string DescribeStored(string noun, string target)
    {
        try
        {
            return _credentials.Exists(target)
                ? "The " + noun + " is already stored in Windows Credential Manager."
                : "No " + noun + " is stored yet.";
        }
        catch (Exception ex)
        {
            return "Could not read Windows Credential Manager. " + ex.Message;
        }
    }

    private static void SelectChoice(System.Windows.Controls.ComboBox box, object value)
    {
        foreach (var item in box.Items)
        {
            if (item is Choice choice && Equals(choice.Value, value))
            {
                box.SelectedItem = choice;
                return;
            }
        }

        if (box.Items.Count > 0)
        {
            box.SelectedIndex = 0;
        }
    }

    private static T SelectedChoice<T>(System.Windows.Controls.ComboBox box, T fallback) where T : struct
    {
        return box.SelectedItem is Choice choice && choice.Value is T value ? value : fallback;
    }

    private static AppSettings Clone(AppSettings source)
    {
        return new AppSettings
        {
            CaptureFolder = source.CaptureFolder,
            FixedRegionWidth = source.FixedRegionWidth,
            FixedRegionHeight = source.FixedRegionHeight,
            CaptureDelaySeconds = source.CaptureDelaySeconds,
            EnableTimingLogs = source.EnableTimingLogs,
            AutoSaveCaptures = source.AutoSaveCaptures,
            JpegQuality = source.JpegQuality,
            LastSaveFolder = source.LastSaveFolder,
            Smtp = source.Smtp?.Copy() ?? new SmtpSettings(),
            Ftp = source.Ftp?.Copy() ?? new FtpSettings(),
            Sftp = source.Sftp?.Copy() ?? new SftpSettings(),
            Hotkeys = source.Hotkeys.ToDictionary(pair => pair.Key, pair => pair.Value)
        };
    }

    private sealed class Choice
    {
        public Choice(object value, string label)
        {
            Value = value;
            Label = label;
        }

        public object Value { get; }

        public string Label { get; }
    }
}
