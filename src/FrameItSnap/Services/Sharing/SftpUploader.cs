using FrameItSnap.Models;

namespace FrameItSnap.Services.Sharing;

public interface ISftpUploader
{
    bool IsSupported { get; }

    string UnavailableReason { get; }

    void Upload(SftpSettings settings, string secret, string localPath, string remoteFileName);
}

/// <summary>
/// SFTP needs an SSH library such as SSH.NET. That is a new NuGet package, which M3 does not add.
/// FTP upload uses the built-in client. Settings and Credential Manager secrets are still saved.
/// </summary>
public sealed class UnsupportedSftpUploader : ISftpUploader
{
    public const string Explanation =
        "SFTP upload is not available in this build. " +
        "Transferring files over SSH needs a library such as SSH.NET, and FrameIt Snap does not add NuGet packages without owner approval. " +
        "Upload (FTP) works with the built-in .NET FTP client. " +
        "You can still save the SFTP host, port, remote path, username, and a password or private-key passphrase. " +
        "Those secrets go to Windows Credential Manager, not settings.json, so a later build can use them after a package is approved.";

    public bool IsSupported => false;

    public string UnavailableReason => Explanation;

    public void Upload(SftpSettings settings, string secret, string localPath, string remoteFileName)
    {
        throw new NotSupportedException(Explanation);
    }
}
