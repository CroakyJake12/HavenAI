using System.Text;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class WindowsProviderSecretStoreCodecTests
{
    [Fact]
    public void MaximumCredentialBlobSizeRoundTripsExactly()
    {
        var secret = new string('a', 1280);
        var bytes = WindowsProviderSecretStore.EncodeSecret(secret);

        Assert.Equal(2560, bytes.Length);
        Assert.Equal(secret, WindowsProviderSecretStore.DecodeSecret(bytes));
    }

    [Fact]
    public void SecretBeyondCredentialBlobLimitIsRejected()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            WindowsProviderSecretStore.EncodeSecret(new string('a', 1281)));

        Assert.Contains("2560 bytes", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("key\0suffix")]
    public void EmptyWhitespaceAndEmbeddedNullSecretsAreRejected(string secret)
    {
        Assert.Throws<ArgumentException>(() =>
            WindowsProviderSecretStore.EncodeSecret(secret));
    }

    [Fact]
    public void InvalidUtf16InputIsRejected()
    {
        var invalid = new byte[] { 0x00, 0xD8 };

        Assert.Throws<InvalidDataException>(() =>
            WindowsProviderSecretStore.DecodeSecret(invalid));
    }

    [Fact]
    public void OddAndOversizedCredentialBlobsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            WindowsProviderSecretStore.DecodeSecret([0x41]));
        Assert.Throws<InvalidDataException>(() =>
            WindowsProviderSecretStore.DecodeSecret(new byte[2562]));
    }

    [Fact]
    public void DecodedWhitespaceAndEmbeddedNullValuesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            WindowsProviderSecretStore.DecodeSecret(Encoding.Unicode.GetBytes("   ")));
        Assert.Throws<InvalidDataException>(() =>
            WindowsProviderSecretStore.DecodeSecret(Encoding.Unicode.GetBytes("key\0suffix")));
    }
}
