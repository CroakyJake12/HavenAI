using System.Security.Cryptography;
using System.Text;

namespace Haven.Application;

public sealed class SettingsEncryptionService
{
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
