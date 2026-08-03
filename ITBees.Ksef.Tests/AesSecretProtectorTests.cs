using System.Security.Cryptography;
using ITBees.Ksef.Credentials.Security;
using Xunit;

namespace ITBees.Ksef.Tests;

public class AesSecretProtectorTests
{
    [Fact]
    public void Roundtrip_returns_the_original_secret()
    {
        var protector = new AesSecretProtector(NewKey());
        const string secret = "KSEF-TOKEN-ĄĆĘŁŃÓŚŹŻ-1234";

        Assert.Equal(secret, protector.Unprotect(protector.Protect(secret)));
    }

    /// <summary>Two encryptions of the same text must differ, or equal ciphertexts in the database
    /// would tell an observer which companies share a token.</summary>
    [Fact]
    public void Same_secret_encrypts_to_different_values()
    {
        var protector = new AesSecretProtector(NewKey());

        Assert.NotEqual(protector.Protect("token"), protector.Protect("token"));
    }

    [Fact]
    public void Another_key_cannot_read_the_secret()
    {
        var stored = new AesSecretProtector(NewKey()).Protect("token");

        Assert.Throws<AuthenticationTagMismatchException>(() => new AesSecretProtector(NewKey()).Unprotect(stored));
    }

    [Fact]
    public void Tampered_value_is_rejected_rather_than_decrypted()
    {
        var protector = new AesSecretProtector(NewKey());
        var stored = protector.Protect("token");
        var tampered = stored[..^2] + (stored[^2] == 'A' ? "B=" : "A=");

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(tampered));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64!")]
    public void Unusable_key_fails_at_construction_not_at_first_save(string? key) =>
        Assert.Throws<InvalidOperationException>(() => new AesSecretProtector(key));

    [Fact]
    public void Key_of_the_wrong_length_is_rejected()
    {
        var tooShort = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        var error = Assert.Throws<InvalidOperationException>(() => new AesSecretProtector(tooShort));
        Assert.Contains("32 bytes", error.Message);
    }

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
