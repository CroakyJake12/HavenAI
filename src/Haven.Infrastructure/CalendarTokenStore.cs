using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>Stores OAuth tokens encrypted for the current Windows user via DPAPI.</summary>
public sealed class WindowsCalendarTokenStore : ICalendarTokenStore
{
    private readonly string _directory;

    public WindowsCalendarTokenStore(IAppPaths paths)
    {
        _directory = Path.Combine(paths.DataDirectory, "CalendarTokens");
    }

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

    public Task DeleteAsync(Guid accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(accountId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetPath(Guid accountId) => Path.Combine(_directory, accountId.ToString("N") + ".token");

    private static byte[] Protect(byte[] value) => Transform(value, protect: true);
    private static byte[] Unprotect(byte[] value) => Transform(value, protect: false);

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

    private const int CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string description, IntPtr optionalEntropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, IntPtr optionalEntropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
