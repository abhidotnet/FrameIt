using System.IO;
using System.Net;
using FrameItSnap.Models;

namespace FrameItSnap.Services.Sharing;

// FtpWebRequest is obsolete, but it is the BCL FTP client used for M3. No FTP package is added.
#pragma warning disable SYSLIB0014

public static class FtpUploader
{
    public static void Upload(FtpSettings settings, string password, string localPath, string remoteFileName)
    {
        var problem = settings.GetConfigurationError();
        if (problem is not null)
        {
            throw new InvalidOperationException(problem);
        }

        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException(
                "Save the FTP password in Settings → Upload. FrameIt Snap stores it in Windows Credential Manager and does not write it to settings.json.");
        }

        if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
        {
            throw new InvalidOperationException("The image to upload could not be prepared.");
        }

        if (string.IsNullOrWhiteSpace(remoteFileName) ||
            remoteFileName is "." or ".." ||
            remoteFileName.IndexOfAny(new[] { '/', '\\' }) >= 0)
        {
            throw new InvalidOperationException("The upload file name is not valid.");
        }

        try
        {
            UploadCore(settings, password, localPath, remoteFileName);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            var detail = ex.Message;
            if (ex is WebException web && web.Response is FtpWebResponse response && !string.IsNullOrWhiteSpace(response.StatusDescription))
            {
                detail = response.StatusDescription.Trim();
            }

            throw new InvalidOperationException("FTP upload failed. " + detail, ex);
        }
    }

    private static void UploadCore(FtpSettings settings, string password, string localPath, string remoteFileName)
    {
        var uri = BuildUri(settings, remoteFileName);

        // Proxy is cleared so the upload goes only to the host the user configured.
        var request = (FtpWebRequest)WebRequest.Create(uri);
        request.Method = WebRequestMethods.Ftp.UploadFile;
        request.Credentials = new NetworkCredential(settings.Username, password);
        request.UseBinary = true;
        request.UsePassive = true;
        request.KeepAlive = false;
        request.Proxy = null;
        request.Timeout = 60_000;
        request.ReadWriteTimeout = 60_000;

        using (var input = File.OpenRead(localPath))
        {
            request.ContentLength = input.Length;
            using var output = request.GetRequestStream();
            input.CopyTo(output);
        }

        using var response = (FtpWebResponse)request.GetResponse();
        var code = (int)response.StatusCode;
        if (code < 200 || code >= 300)
        {
            var description = response.StatusDescription;
            throw new InvalidOperationException("FTP upload failed. " + (string.IsNullOrWhiteSpace(description) ? "The server rejected the upload." : description.Trim()));
        }
    }

    private static Uri BuildUri(FtpSettings settings, string remoteFileName)
    {
        var directory = settings.RemotePath.Replace('\\', '/').Trim();
        if (directory.Length == 0)
        {
            directory = "/";
        }

        if (!directory.StartsWith('/'))
        {
            directory = "/" + directory;
        }

        if (!directory.EndsWith('/'))
        {
            directory += "/";
        }

        return new UriBuilder(Uri.UriSchemeFtp, settings.Host, settings.Port, directory + remoteFileName).Uri;
    }
}
