using System.IO;
using System.Windows;
using FrameIt.Editing;
using FrameIt.Services;
using FrameIt.Services.Sharing;

namespace FrameIt.UI;

public partial class EditorWindow
{
    private bool _shareInProgress;

    private async void ShareEmail_OnClick(object sender, RoutedEventArgs e)
    {
        await ShareAsync(ShareKind.Email);
    }

    private async void ShareFtp_OnClick(object sender, RoutedEventArgs e)
    {
        await ShareAsync(ShareKind.Ftp);
    }

    private async void ShareSftp_OnClick(object sender, RoutedEventArgs e)
    {
        await ShareAsync(ShareKind.Sftp);
    }

    private async Task ShareAsync(ShareKind kind)
    {
        if (_shareInProgress)
        {
            return;
        }

        _shareInProgress = true;
        string? tempPath = null;
        try
        {
            if (kind == ShareKind.Sftp)
            {
                // SFTP has no BCL client. This type is the stand-in until an SSH package is approved.
                ISftpUploader uploader = new UnsupportedSftpUploader();
                if (!uploader.IsSupported)
                {
                    if (IsLoaded)
                    {
                        System.Windows.MessageBox.Show(
                            this,
                            uploader.UnavailableReason,
                            "FrameIt",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }

                    return;
                }

                throw new NotSupportedException("SFTP is marked supported, but this build has no upload call.");
            }

            var settings = _getSettings();
            var configError = kind == ShareKind.Email
                ? settings.Smtp.IsBlank
                    ? "Set the SMTP host, From address, and default To address in Settings → Email."
                    : settings.Smtp.GetConfigurationError(requireRecipient: true)
                : settings.Ftp.IsBlank
                    ? "Set the FTP host and username in Settings → Upload."
                    : settings.Ftp.GetConfigurationError();
            if (configError is not null)
            {
                ShowShareError(configError);
                return;
            }

            if (!TryReadShareSecret(kind, out var password, out var failure))
            {
                ShowShareError(failure);
                return;
            }

            if (!TryCreateShareFile(out tempPath, out var displayName) || tempPath is null)
            {
                return;
            }

            if (IsLoaded)
            {
                StatusText.Text = kind == ShareKind.Email ? "Sending email…" : "Uploading…";
            }

            var path = tempPath;
            var name = displayName;
            var previousCursor = Cursor;
            Cursor = System.Windows.Input.Cursors.Wait;
            try
            {
                await Task.Run(() =>
                {
                    if (kind == ShareKind.Email)
                    {
                        SmtpMailSender.Send(settings.Smtp, password, path, name);
                    }
                    else
                    {
                        FtpUploader.Upload(settings.Ftp, password, path, name);
                    }
                });
            }
            finally
            {
                Cursor = previousCursor;
            }

            if (IsLoaded)
            {
                StatusText.Text = kind == ShareKind.Email
                    ? "Emailed the image to " + settings.Smtp.DefaultToAddress + "."
                    : "Uploaded " + name + " to " + settings.Ftp.Host + ".";
            }
        }
        catch (Exception ex)
        {
            ShowShareError(ex.Message);
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            _shareInProgress = false;
        }
    }

    private bool TryReadShareSecret(ShareKind kind, out string password, out string failure)
    {
        password = string.Empty;
        failure = string.Empty;
        var target = kind == ShareKind.Email ? CredentialNames.SmtpPassword : CredentialNames.FtpPassword;
        var which = kind == ShareKind.Email ? "SMTP password in Settings → Email" : "FTP password in Settings → Upload";
        try
        {
            var store = new WindowsCredentialStore();
            if (!store.TryRead(target, out password) || password.Length == 0)
            {
                failure = "Save the " + which + ". FrameIt stores it in Windows Credential Manager and does not write it to settings.json.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            return false;
        }
    }

    private bool TryCreateShareFile(out string? tempPath, out string displayName)
    {
        tempPath = null;
        displayName = string.Empty;
        CommitStyleChange();
        CommitInlineText();
        ApplyLiveCrop();
        if (!TryReadJpegQuality(out var quality))
        {
            return false;
        }

        var jpeg = _filePath is not null && IsJpegPath(_filePath);
        var extension = jpeg ? ".jpg" : ".png";
        displayName = SanitizeFileName(_filePath is not null
            ? Path.GetFileNameWithoutExtension(_filePath) + extension
            : "capture-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + extension);
        var directory = Path.Combine(Path.GetTempPath(), "FrameIt");
        Directory.CreateDirectory(directory);
        tempPath = Path.Combine(directory, Guid.NewGuid().ToString("N") + extension);
        using var flattened = ImageEffects.Flatten(_session.Image, _session.Redactions, _session.Annotations);
        ImageEffects.Save(flattened, tempPath, quality);
        return true;
    }

    private void ShowShareError(string message)
    {
        if (!IsLoaded || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        UpdateStatus();
        System.Windows.MessageBox.Show(this, message, "FrameIt", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var cleaned = new string(chars).Trim();
        return string.IsNullOrEmpty(cleaned) ? "capture.png" : cleaned;
    }

    private enum ShareKind
    {
        Email,
        Ftp,
        Sftp
    }
}
