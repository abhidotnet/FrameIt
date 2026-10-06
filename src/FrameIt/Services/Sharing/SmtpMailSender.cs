using System.IO;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using FrameIt.Models;

namespace FrameIt.Services.Sharing;

// SmtpClient is obsolete, but it is the BCL mail client used for M3. No mail package is added.
#pragma warning disable SYSLIB0014

public static class SmtpMailSender
{
    public const string SecurityNote =
        "STARTTLS (usually port 587) is supported. The built-in mail client does not support implicit TLS on port 465.";

    public static void Send(SmtpSettings settings, string password, string attachmentPath, string attachmentName)
    {
        var problem = settings.GetConfigurationError(requireRecipient: true);
        if (problem is not null)
        {
            throw new InvalidOperationException(problem);
        }

        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException(
                "Save the SMTP password in Settings → Email. FrameIt stores it in Windows Credential Manager and does not write it to settings.json.");
        }

        if (string.IsNullOrWhiteSpace(attachmentPath) || !File.Exists(attachmentPath))
        {
            throw new InvalidOperationException("The image to attach could not be prepared.");
        }

        try
        {
            SendCore(settings, password, attachmentPath, attachmentName);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException("Could not send the email. " + ex.Message, ex);
        }
    }

    private static void SendCore(SmtpSettings settings, string password, string attachmentPath, string attachmentName)
    {
        // EnableSsl is STARTTLS (RFC 3207). Implicit TLS (SMTPS, typically port 465) is not supported.
        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            EnableSsl = settings.Security == SmtpSecurityMode.StartTls,
            Timeout = 60_000,
            Credentials = new NetworkCredential(
                string.IsNullOrWhiteSpace(settings.Username) ? settings.FromAddress : settings.Username,
                password)
        };

        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress),
            Subject = "FrameIt capture: " + attachmentName,
            Body = "The captured image is attached.",
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8
        };

        foreach (var address in SmtpSettings.SplitAddresses(settings.DefaultToAddress))
        {
            message.To.Add(address);
        }

        var mediaType = IsJpeg(attachmentName) ? MediaTypeNames.Image.Jpeg : MediaTypeNames.Image.Png;
        var attachment = new Attachment(attachmentPath, mediaType)
        {
            Name = attachmentName
        };
        if (attachment.ContentDisposition is not null)
        {
            attachment.ContentDisposition.FileName = attachmentName;
        }

        message.Attachments.Add(attachment);
        client.Send(message);
    }

    private static bool IsJpeg(string name)
    {
        var extension = Path.GetExtension(name);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }
}
