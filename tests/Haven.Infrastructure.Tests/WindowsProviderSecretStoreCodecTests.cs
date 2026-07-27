/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/WindowsProviderSecretStoreCodecTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns WindowsProviderSecretStoreCodecTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents windows provider secret store codec tests and keeps its related state and behavior together.
/// </summary>
public sealed class WindowsProviderSecretStoreCodecTests
{
    /// <summary>
    /// Performs the maximum credential blob size round trips exactly step owned by this component.
    /// </summary>
    [Fact]
    public void MaximumCredentialBlobSizeRoundTripsExactly()
    {
        var secret = new string('a', 1280);
        var bytes = WindowsProviderSecretStore.EncodeSecret(secret);

        Assert.Equal(2560, bytes.Length);
        Assert.Equal(secret, WindowsProviderSecretStore.DecodeSecret(bytes));
    }

    /// <summary>
    /// Performs the secret beyond credential blob limit is rejected step owned by this component.
    /// </summary>
    [Fact]
    public void SecretBeyondCredentialBlobLimitIsRejected()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            WindowsProviderSecretStore.EncodeSecret(new string('a', 1281)));

        Assert.Contains("2560 bytes", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Performs the empty whitespace and embedded null secrets are rejected step owned by this component.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("key\0suffix")]
    public void EmptyWhitespaceAndEmbeddedNullSecretsAreRejected(string secret)
    {
        Assert.Throws<ArgumentException>(() =>
            WindowsProviderSecretStore.EncodeSecret(secret));
    }

    /// <summary>
    /// Performs the invalid utf16 input is rejected step owned by this component.
    /// </summary>
    [Fact]
    public void InvalidUtf16InputIsRejected()
    {
        var invalid = new byte[] { 0x00, 0xD8 };

        Assert.Throws<InvalidDataException>(() =>
            WindowsProviderSecretStore.DecodeSecret(invalid));
    }

    /// <summary>
    /// Performs the odd and oversized credential blobs are rejected step owned by this component.
    /// </summary>
    [Fact]
    public void OddAndOversizedCredentialBlobsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            WindowsProviderSecretStore.DecodeSecret([0x41]));
        Assert.Throws<InvalidDataException>(() =>
            WindowsProviderSecretStore.DecodeSecret(new byte[2562]));
    }

    /// <summary>
    /// Performs the decoded whitespace and embedded null values are rejected step owned by this component.
    /// </summary>
    [Fact]
    public void DecodedWhitespaceAndEmbeddedNullValuesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            WindowsProviderSecretStore.DecodeSecret(Encoding.Unicode.GetBytes("   ")));
        Assert.Throws<InvalidDataException>(() =>
            WindowsProviderSecretStore.DecodeSecret(Encoding.Unicode.GetBytes("key\0suffix")));
    }
}
