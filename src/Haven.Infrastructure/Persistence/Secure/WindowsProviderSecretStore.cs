/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/WindowsProviderSecretStore.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns WindowsProviderSecretStore, Credential. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>
/// Represents windows provider secret store and keeps its related state and behavior together.
/// </summary>
public sealed class WindowsProviderSecretStore : IProviderSecretStore
{
    /// <summary>
    /// Stores credential type generic locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const uint CredentialTypeGeneric = 1;
    /// <summary>
    /// Stores persist local machine locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const uint PersistLocalMachine = 2;
    /// <summary>
    /// Stores max credential blob bytes locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MaxCredentialBlobBytes = 2560;
    /// <summary>
    /// Stores strict unicode locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly UnicodeEncoding StrictUnicode = new(
        bigEndian: false,
        byteOrderMark: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// Performs set asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Retrieves async for the current operation.
    /// </summary>
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

    /// <summary>
    /// Performs delete asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs the encode secret step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the decode secret step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the target step owned by this component.
    /// </summary>
    private static string Target(string providerId, string secretName) => $"Haven.ModelProvider|{providerId.ToLowerInvariant()}|{secretName.ToLowerInvariant()}";

    /// <summary>
    /// Validates name before it crosses the next trust or persistence boundary.
    /// </summary>
    private static void ValidateName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80 || value.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.')))
            throw new ArgumentException("Credential identifiers may contain letters, numbers, dash, underscore, or dot.", parameterName);
    }

    /// <summary>
    /// Performs the ensure windows step owned by this component.
    /// </summary>
    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Provider credential storage requires Windows Credential Manager.");
    }

    /// <summary>
    /// Represents credential and keeps its related state and behavior together.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        /// <summary>
        /// Stores flags locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public uint Flags;
        /// <summary>
        /// Stores type locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public uint Type;
        /// <summary>
        /// Stores target name locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public string TargetName;
        /// <summary>
        /// Stores comment locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public string? Comment;
        /// <summary>
        /// Stores last written locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        /// <summary>
        /// Stores credential blob size locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public uint CredentialBlobSize;
        /// <summary>
        /// Stores credential blob locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public IntPtr CredentialBlob;
        /// <summary>
        /// Stores persist locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public uint Persist;
        /// <summary>
        /// Stores attribute count locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public uint AttributeCount;
        /// <summary>
        /// Stores attributes locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public IntPtr Attributes;
        /// <summary>
        /// Stores target alias locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public string? TargetAlias;
        /// <summary>
        /// Stores user name locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public string UserName;
    }

    /// <summary>
    /// Performs the cred write step owned by this component.
    /// </summary>
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite([In] ref Credential credential, uint flags);

    /// <summary>
    /// Performs the cred read step owned by this component.
    /// </summary>
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPointer);

    /// <summary>
    /// Performs the cred delete step owned by this component.
    /// </summary>
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    /// <summary>
    /// Performs the cred free step owned by this component.
    /// </summary>
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
