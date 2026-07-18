using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Haven.Application;

namespace Haven.Infrastructure;

public sealed class WindowsProviderSecretStore : IProviderSecretStore
{
    private const uint CredentialTypeGeneric = 1;
    private const uint PersistLocalMachine = 2;
    private const int MaxCredentialBlobBytes = 2560;
    private static readonly UnicodeEncoding StrictUnicode = new(
        bigEndian: false,
        byteOrderMark: false,
        throwOnInvalidBytes: true);

    public Task SetAsync(string providerId, string secretName, string secret, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWindows();
        ValidateName(providerId, nameof(providerId));
        ValidateName(secretName, nameof(secretName));

        var bytes = EncodeSecret(secret);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = CredentialTypeGeneric,
                TargetName = Target(providerId, secretName),
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = "Haven"
            };
            if (!CredWrite(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            Array.Clear(bytes);
            if (blob != IntPtr.Zero)
            {
                for (var index = 0; index < bytes.Length; index++) Marshal.WriteByte(blob, index, 0);
                Marshal.FreeCoTaskMem(blob);
            }
        }
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string providerId, string secretName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateName(providerId, nameof(providerId));
        ValidateName(secretName, nameof(secretName));
        if (!OperatingSystem.IsWindows()) return Task.FromResult<string?>(null);
        if (!CredRead(Target(providerId, secretName), CredentialTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 1168) return Task.FromResult<string?>(null);
            throw new Win32Exception(error);
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero) return Task.FromResult<string?>(null);
            var size = checked((int)credential.CredentialBlobSize);
            var bytes = new byte[size];
            try
            {
                Marshal.Copy(credential.CredentialBlob, bytes, 0, size);
                return Task.FromResult<string?>(DecodeSecret(bytes));
            }
            finally
            {
                Array.Clear(bytes);
            }
        }
        finally { CredFree(pointer); }
    }

    public Task DeleteAsync(string providerId, string secretName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateName(providerId, nameof(providerId));
        ValidateName(secretName, nameof(secretName));
        if (!OperatingSystem.IsWindows()) return Task.CompletedTask;
        if (!CredDelete(Target(providerId, secretName), CredentialTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1168) throw new Win32Exception(error);
        }
        return Task.CompletedTask;
    }

    public static byte[] EncodeSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("Secret value is required and cannot be only whitespace.", nameof(secret));
        if (secret.Contains('\0'))
            throw new ArgumentException("Secret value cannot contain an embedded null character.", nameof(secret));

        byte[] bytes;
        try
        {
            bytes = StrictUnicode.GetBytes(secret);
        }
        catch (EncoderFallbackException ex)
        {
            throw new ArgumentException("Secret value contains invalid UTF-16 text.", nameof(secret), ex);
        }

        if (bytes.Length > MaxCredentialBlobBytes)
        {
            Array.Clear(bytes);
            throw new ArgumentException(
                $"Provider credential exceeds Windows Credential Manager's {MaxCredentialBlobBytes} bytes generic credential limit.",
                nameof(secret));
        }
        return bytes;
    }

    public static string DecodeSecret(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0 || bytes.Length > MaxCredentialBlobBytes || bytes.Length % 2 != 0)
            throw new InvalidDataException("The Windows credential blob has an invalid UTF-16 byte length.");

        string secret;
        try
        {
            secret = StrictUnicode.GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException("The Windows credential blob contains invalid UTF-16 text.", ex);
        }

        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidDataException("The Windows credential blob decoded to an empty or whitespace-only secret.");
        if (secret.Contains('\0'))
            throw new InvalidDataException("The Windows credential blob contains an embedded null character.");
        return secret;
    }

    private static string Target(string providerId, string secretName) => $"Haven.ModelProvider|{providerId.ToLowerInvariant()}|{secretName.ToLowerInvariant()}";

    private static void ValidateName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80 || value.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.')))
            throw new ArgumentException("Credential identifiers may contain letters, numbers, dash, underscore, or dot.", parameterName);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Provider credential storage requires Windows Credential Manager.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite([In] ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPointer);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
