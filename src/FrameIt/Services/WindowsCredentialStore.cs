using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace FrameIt.Services;

public static class CredentialNames
{
    public const string SmtpPassword = "FrameIt:smtp-password";

    public const string FtpPassword = "FrameIt:ftp-password";

    public const string SftpPassword = "FrameIt:sftp-password";

    public const string SftpPassphrase = "FrameIt:sftp-passphrase";
}

/// <summary>
/// Stores FrameIt secrets in Windows Credential Manager via CredWrite / CredRead / CredDelete.
/// Unpackaged tray apps do not have a package identity, so PasswordVault is not used.
/// Persistence is CRED_PERSIST_LOCAL_MACHINE: the signing-in user on this computer, including after reboot.
/// </summary>
public sealed class WindowsCredentialStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int ErrorFileNotFound = 2;
    private const int MaxSecretBytes = 16 * 1024;

    public bool Exists(string target)
    {
        return TryRead(target, out _);
    }

    public bool TryRead(string target, out string secret)
    {
        secret = string.Empty;
        if (!CredRead(target, CredTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error is ErrorNotFound or ErrorFileNotFound)
            {
                return false;
            }

            throw CredentialFailure("Could not read Windows Credential Manager.", error);
        }

        try
        {
            var native = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (native.CredentialBlob == IntPtr.Zero || native.CredentialBlobSize == 0)
            {
                return false;
            }

            if (native.CredentialBlobSize > MaxSecretBytes)
            {
                throw new InvalidOperationException("The stored FrameIt secret is larger than expected.");
            }

            var bytes = new byte[native.CredentialBlobSize];
            Marshal.Copy(native.CredentialBlob, bytes, 0, bytes.Length);
            try
            {
                secret = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
                return secret.Length > 0;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Write(string target, string userName, string secret, string comment)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new ArgumentException("A credential target is required.", nameof(target));
        }

        if (string.IsNullOrEmpty(secret))
        {
            throw new ArgumentException("A secret is required.", nameof(secret));
        }

        var user = string.IsNullOrWhiteSpace(userName) ? "FrameIt" : userName;
        var blob = Encoding.Unicode.GetBytes(secret);
        var targetPtr = IntPtr.Zero;
        var commentPtr = IntPtr.Zero;
        var userPtr = IntPtr.Zero;
        var blobPtr = IntPtr.Zero;
        try
        {
            targetPtr = Marshal.StringToCoTaskMemUni(target);
            commentPtr = Marshal.StringToCoTaskMemUni(comment ?? string.Empty);
            userPtr = Marshal.StringToCoTaskMemUni(user);
            blobPtr = Marshal.AllocCoTaskMem(blob.Length);
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var native = new NativeCredential
            {
                Type = CredTypeGeneric,
                TargetName = targetPtr,
                Comment = commentPtr,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPtr,
                Persist = CredPersistLocalMachine,
                UserName = userPtr
            };

            if (!CredWrite(ref native, 0))
            {
                throw CredentialFailure("Could not save the secret in Windows Credential Manager.", Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blob);
            Free(targetPtr);
            Free(commentPtr);
            Free(userPtr);
            Free(blobPtr);
        }
    }

    public void Delete(string target)
    {
        if (CredDelete(target, CredTypeGeneric, 0))
        {
            return;
        }

        var error = Marshal.GetLastWin32Error();
        if (error is ErrorNotFound or ErrorFileNotFound)
        {
            return;
        }

        throw CredentialFailure("Could not remove the secret from Windows Credential Manager.", error);
    }

    private static InvalidOperationException CredentialFailure(string action, int error)
    {
        var details = new Win32Exception(error);
        return new InvalidOperationException(action + " " + details.Message, details);
    }

    private static void Free(IntPtr pointer)
    {
        if (pointer != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
}
