using System.IO;
using System.Text.Json.Serialization;

namespace FrameIt.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SmtpSecurityMode
{
    None = 0,
    StartTls = 1
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SftpAuthMode
{
    Password = 0,
    PrivateKey = 1
}

public sealed class SmtpSettings
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public SmtpSecurityMode Security { get; set; } = SmtpSecurityMode.StartTls;

    public string Username { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;

    public string DefaultToAddress { get; set; } = string.Empty;

    public bool IsBlank =>
        string.IsNullOrWhiteSpace(Host) &&
        string.IsNullOrWhiteSpace(Username) &&
        string.IsNullOrWhiteSpace(FromAddress) &&
        string.IsNullOrWhiteSpace(DefaultToAddress);

    public SmtpSettings Copy()
    {
        return new SmtpSettings
        {
            Host = Host,
            Port = Port,
            Security = Security,
            Username = Username,
            FromAddress = FromAddress,
            DefaultToAddress = DefaultToAddress
        };
    }

    public string? GetConfigurationError(bool requireRecipient)
    {
        if (string.IsNullOrWhiteSpace(Host) || !IsHostOnly(Host))
        {
            return "Enter an SMTP host name or IP address only, without a scheme or password.";
        }

        if (Port is < 1 or > 65535)
        {
            return "SMTP port must be a number from 1 to 65535.";
        }

        if (!Enum.IsDefined(Security))
        {
            return "Choose an SMTP security mode.";
        }

        if (string.IsNullOrWhiteSpace(FromAddress) || !LooksLikeAddress(FromAddress))
        {
            return "Enter a From address, for example you@example.com.";
        }

        if (string.IsNullOrWhiteSpace(DefaultToAddress))
        {
            if (requireRecipient)
            {
                return "Enter a default To address in Settings. Share → Email uses it and does not ask again.";
            }
        }
        else
        {
            var addresses = SplitAddresses(DefaultToAddress).ToList();
            if (addresses.Count == 0)
            {
                return "Enter a default To address in Settings. Share → Email uses it and does not ask again.";
            }

            foreach (var address in addresses)
            {
                if (!LooksLikeAddress(address))
                {
                    return "The To address \"" + address + "\" is not a valid email address.";
                }
            }
        }

        return null;
    }

    public static IEnumerable<string> SplitAddresses(string value)
    {
        return value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    internal static bool IsHostOnly(string host)
    {
        var value = host.Trim();
        return value.Length > 0 &&
               !value.Contains("://", StringComparison.Ordinal) &&
               !value.Contains('@') &&
               !value.Contains(' ') &&
               !value.Contains('/') &&
               !value.Contains(':');
    }

    private static bool LooksLikeAddress(string value)
    {
        var at = value.IndexOf('@');
        return at > 0 &&
               at == value.LastIndexOf('@') &&
               at < value.Length - 1 &&
               value.IndexOf('.', at) > at + 1 &&
               !value.Contains(' ');
    }
}

public sealed class FtpSettings
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 21;

    public string RemotePath { get; set; } = "/";

    public string Username { get; set; } = string.Empty;

    public bool IsBlank =>
        string.IsNullOrWhiteSpace(Host) &&
        string.IsNullOrWhiteSpace(Username) &&
        IsDefaultPath(RemotePath);

    public FtpSettings Copy()
    {
        return new FtpSettings
        {
            Host = Host,
            Port = Port,
            RemotePath = RemotePath,
            Username = Username
        };
    }

    public string? GetConfigurationError()
    {
        if (string.IsNullOrWhiteSpace(Host) || !SmtpSettings.IsHostOnly(Host))
        {
            return "Enter an FTP host name or IP address only, without a scheme or password.";
        }

        if (Port is < 1 or > 65535)
        {
            return "FTP port must be a number from 1 to 65535.";
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            return "Enter an FTP username. The password is saved separately in Windows Credential Manager.";
        }

        return null;
    }

    internal static bool IsDefaultPath(string? path)
    {
        var value = path?.Trim().Replace('\\', '/') ?? string.Empty;
        return value.Length == 0 || value == "/";
    }
}

public sealed class SftpSettings
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 22;

    public string RemotePath { get; set; } = "/";

    public string Username { get; set; } = string.Empty;

    public SftpAuthMode AuthMode { get; set; } = SftpAuthMode.Password;

    public string PrivateKeyPath { get; set; } = string.Empty;

    public bool IsBlank =>
        string.IsNullOrWhiteSpace(Host) &&
        string.IsNullOrWhiteSpace(Username) &&
        string.IsNullOrWhiteSpace(PrivateKeyPath) &&
        FtpSettings.IsDefaultPath(RemotePath);

    public SftpSettings Copy()
    {
        return new SftpSettings
        {
            Host = Host,
            Port = Port,
            RemotePath = RemotePath,
            Username = Username,
            AuthMode = AuthMode,
            PrivateKeyPath = PrivateKeyPath
        };
    }

    public string? GetConfigurationError()
    {
        if (string.IsNullOrWhiteSpace(Host) || !SmtpSettings.IsHostOnly(Host))
        {
            return "Enter an SFTP host name or IP address only, without a scheme or password.";
        }

        if (Port is < 1 or > 65535)
        {
            return "SFTP port must be a number from 1 to 65535.";
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            return "Enter an SFTP username.";
        }

        if (!Enum.IsDefined(AuthMode))
        {
            return "Choose an SFTP authentication mode.";
        }

        if (AuthMode == SftpAuthMode.PrivateKey)
        {
            if (string.IsNullOrWhiteSpace(PrivateKeyPath))
            {
                return "Enter the path to the SFTP private key. The passphrase is saved in Windows Credential Manager.";
            }

            if (!File.Exists(PrivateKeyPath))
            {
                return "The SFTP private key file was not found. FrameIt stores the path only, not the key.";
            }
        }

        return null;
    }
}
