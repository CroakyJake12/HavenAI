using System.Text;
using System.Text.Json;
using Android.Content;
using Android.Security.Keystore;
using Haven.Application;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Interfaces;
using Javax.Crypto.Spec;

namespace Haven.Android;

/// <summary>
/// Encrypts small credential values with a non-exportable Android Keystore AES-GCM key.
/// Only authenticated ciphertext and its random IV are stored in private app preferences.
/// </summary>
public sealed class AndroidEncryptedPreferenceStore
{
    private const string KeyAlias = "haven.credentials.aes-gcm.v1";
    private const string PreferenceName = "haven.encrypted.credentials.v1";

    public Task SetAsync(string key, string value, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")
                           ?? throw new InvalidOperationException("AES-GCM is not available on this Android device.");
        cipher.Init(CipherMode.EncryptMode, GetOrCreateKey());
        var encrypted = cipher.DoFinal(Encoding.UTF8.GetBytes(value))
                        ?? throw new InvalidOperationException("Android Keystore returned no ciphertext.");
        var iv = cipher.GetIV() ?? throw new InvalidOperationException("Android Keystore returned no initialization vector.");
        var encoded = Convert.ToBase64String(iv) + ":" + Convert.ToBase64String(encrypted);
        using var editor = Preferences.Edit() ?? throw new InvalidOperationException("Android secure preferences are unavailable.");
        var pending = editor.PutString(key, encoded) ?? throw new InvalidOperationException("Android secure preferences rejected the credential value.");
        if (!pending.Commit())
            throw new IOException("Android could not persist the encrypted credential.");
    }, cancellationToken);

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var encoded = Preferences.GetString(key, null);
        if (string.IsNullOrWhiteSpace(encoded)) return null;
        var separator = encoded.IndexOf(':');
        if (separator <= 0 || separator == encoded.Length - 1)
            throw new InvalidDataException("The encrypted credential payload is malformed.");
        var iv = Convert.FromBase64String(encoded[..separator]);
        var encrypted = Convert.FromBase64String(encoded[(separator + 1)..]);
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")
                           ?? throw new InvalidOperationException("AES-GCM is not available on this Android device.");
        using var parameters = new GCMParameterSpec(128, iv);
        cipher.Init(CipherMode.DecryptMode, GetOrCreateKey(), parameters);
        var plaintext = cipher.DoFinal(encrypted)
                        ?? throw new InvalidOperationException("Android Keystore returned no plaintext.");
        return Encoding.UTF8.GetString(plaintext);
    }, cancellationToken);

    public Task DeleteAsync(string key, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var editor = Preferences.Edit() ?? throw new InvalidOperationException("Android secure preferences are unavailable.");
        var pending = editor.Remove(key) ?? throw new InvalidOperationException("Android secure preferences rejected the credential deletion.");
        if (!pending.Commit())
            throw new IOException("Android could not delete the encrypted credential.");
    }, cancellationToken);

    private static ISharedPreferences Preferences =>
        global::Android.App.Application.Context.GetSharedPreferences(PreferenceName, FileCreationMode.Private)
        ?? throw new InvalidOperationException("Android secure preferences are unavailable.");

    private static ISecretKey GetOrCreateKey()
    {
        using var keyStore = KeyStore.GetInstance("AndroidKeyStore")
                             ?? throw new InvalidOperationException("Android Keystore is unavailable.");
        keyStore.Load(null);
        if (keyStore.GetKey(KeyAlias, null) is ISecretKey existing) return existing;

        using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")
                              ?? throw new InvalidOperationException("Android Keystore AES key generation is unavailable.");
        using var builder = new KeyGenParameterSpec.Builder(KeyAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt);
        using var specification = builder
            .SetKeySize(256)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetRandomizedEncryptionRequired(true)
            .Build();
        generator.Init(specification);
        return generator.GenerateKey() as ISecretKey
               ?? throw new InvalidOperationException("Android Keystore did not create an AES key.");
    }
}

public sealed class AndroidProviderSecretStore(AndroidEncryptedPreferenceStore store) : IProviderSecretStore
{
    public Task SetAsync(string providerId, string secretName, string secret, CancellationToken cancellationToken) =>
        store.SetAsync(ProviderKey(providerId, secretName), secret, cancellationToken);
    public Task<string?> GetAsync(string providerId, string secretName, CancellationToken cancellationToken) =>
        store.GetAsync(ProviderKey(providerId, secretName), cancellationToken);
    public Task DeleteAsync(string providerId, string secretName, CancellationToken cancellationToken) =>
        store.DeleteAsync(ProviderKey(providerId, secretName), cancellationToken);

    private static string ProviderKey(string providerId, string secretName) =>
        "provider:" + Uri.EscapeDataString(providerId) + ":" + Uri.EscapeDataString(secretName);
}

public sealed class AndroidCalendarTokenStore(AndroidEncryptedPreferenceStore store) : ICalendarTokenStore
{
    public Task SaveAsync(Guid accountId, CalendarTokenEnvelope token, CancellationToken cancellationToken) =>
        store.SetAsync(TokenKey(accountId), JsonSerializer.Serialize(token), cancellationToken);

    public async Task<CalendarTokenEnvelope?> GetAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var payload = await store.GetAsync(TokenKey(accountId), cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(payload) ? null : JsonSerializer.Deserialize<CalendarTokenEnvelope>(payload);
    }

    public Task DeleteAsync(Guid accountId, CancellationToken cancellationToken) =>
        store.DeleteAsync(TokenKey(accountId), cancellationToken);

    private static string TokenKey(Guid accountId) => "calendar:" + accountId.ToString("N");
}
