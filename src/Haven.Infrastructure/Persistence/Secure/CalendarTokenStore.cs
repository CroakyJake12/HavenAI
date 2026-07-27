/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/CalendarTokenStore.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns WindowsCalendarTokenStore, DataBlob. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>Stores OAuth tokens encrypted for the current Windows user via DPAPI.</summary>
public sealed class WindowsCalendarTokenStore : ICalendarTokenStore
{
    /// <summary>
    /// Stores directory locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _directory;

    public WindowsCalendarTokenStore(IAppPaths paths)
    {
        _directory = Path.Combine(paths.DataDirectory, "CalendarTokens");
    }

    /// <summary>
    /// Performs save asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task SaveAsync(Guid accountId, CalendarTokenEnvelope token, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Calendar token encryption requires Windows DPAPI.");
        Directory.CreateDirectory(_directory);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(token);
        try
        {
            var protectedBytes = Protect(plaintext);
            var path = GetPath(accountId);
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllBytesAsync(temporary, protectedBytes, cancellationToken).ConfigureAwait(false);
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        finally { Array.Clear(plaintext); }
    }

    /// <summary>
    /// Retrieves async for the current operation.
    /// </summary>
    public async Task<CalendarTokenEnvelope?> GetAsync(Guid accountId, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var path = GetPath(accountId);
        if (!File.Exists(path)) return null;
        var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var plaintext = Unprotect(protectedBytes);
        try { return JsonSerializer.Deserialize<CalendarTokenEnvelope>(plaintext); }
        finally { Array.Clear(plaintext); }
    }

    /// <summary>
    /// Performs delete asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DeleteAsync(Guid accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(accountId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Retrieves path for the current operation.
    /// </summary>
    private string GetPath(Guid accountId) => Path.Combine(_directory, accountId.ToString("N") + ".token");

    /// <summary>
    /// Performs the protect step owned by this component.
    /// </summary>
    private static byte[] Protect(byte[] value) => Transform(value, protect: true);
    /// <summary>
    /// Performs the unprotect step owned by this component.
    /// </summary>
    private static byte[] Unprotect(byte[] value) => Transform(value, protect: false);

    /// <summary>
    /// Performs the transform step owned by this component.
    /// </summary>
    private static byte[] Transform(byte[] value, bool protect)
    {
        var input = new DataBlob();
        var output = new DataBlob();
        try
        {
            input.Size = value.Length;
            input.Data = Marshal.AllocHGlobal(value.Length);
            Marshal.Copy(value, 0, input.Data, value.Length);
            var succeeded = protect
                ? CryptProtectData(ref input, "Haven Calendar OAuth token", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output);
            if (!succeeded) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        finally
        {
            if (input.Data != IntPtr.Zero)
            {
                for (var index = 0; index < input.Size; index++) Marshal.WriteByte(input.Data, index, 0);
                Marshal.FreeHGlobal(input.Data);
            }
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }

    /// <summary>
    /// Stores crypt protect ui forbidden locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int CryptProtectUiForbidden = 0x1;

    /// <summary>
    /// Represents data blob and keeps its related state and behavior together.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        /// <summary>
        /// Stores size locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public int Size;
        /// <summary>
        /// Stores data locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public IntPtr Data;
    }

    /// <summary>
    /// Performs the crypt protect data step owned by this component.
    /// </summary>
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string description, IntPtr optionalEntropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    /// <summary>
    /// Performs the crypt unprotect data step owned by this component.
    /// </summary>
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, IntPtr optionalEntropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    /// <summary>
    /// Performs the local free step owned by this component.
    /// </summary>
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
