/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/SettingsEncryptionService.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns SettingsEncryptionService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Security.Cryptography;
using System.Text;

namespace Haven.Application;

/// <summary>
/// Represents settings encryption service and keeps its related state and behavior together.
/// </summary>
public sealed class SettingsEncryptionService
{
    /// <summary>
    /// Stores salt locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly byte[] Salt = "HavenSettingsSalt2024"u8.ToArray();

    private static (byte[] Key, byte[] Iv) DeriveKey(string passphrase)
    {
        var combined = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase),
            Salt,
            100000,
            HashAlgorithmName.SHA256,
            48);
        var key = combined[..32];
        var iv = combined[32..48];
        return (key, iv);
    }

    /// <summary>
    /// Performs the encrypt step owned by this component.
    /// </summary>
    public string Encrypt(string plainText, string passphrase)
    {
        var (key, iv) = DeriveKey(passphrase);
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        using var encryptor = aes.CreateEncryptor();
        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var writer = new StreamWriter(cs, Encoding.UTF8))
        {
            writer.Write(plainText);
        }
        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>
    /// Performs the decrypt step owned by this component.
    /// </summary>
    public string Decrypt(string cipherText, string passphrase)
    {
        var (key, iv) = DeriveKey(passphrase);
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        using var ms = new MemoryStream(Convert.FromBase64String(cipherText));
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var reader = new StreamReader(cs, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
