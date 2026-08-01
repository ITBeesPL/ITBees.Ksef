using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ITBees.Ksef.Security;
using Xunit;

namespace ITBees.Ksef.Tests;

public class KsefCryptographyTests
{
    [Fact]
    public void GenerateAes256Key_Returns32Bytes()
    {
        Assert.Equal(32, KsefCryptography.GenerateAes256Key().Length);
    }

    [Fact]
    public void GenerateIv_Returns16Bytes()
    {
        Assert.Equal(16, KsefCryptography.GenerateIv().Length);
    }

    [Fact]
    public void EncryptAes256Cbc_RoundTripsWithDecrypt()
    {
        var key = KsefCryptography.GenerateAes256Key();
        var iv = KsefCryptography.GenerateIv();
        var plaintext = Encoding.UTF8.GetBytes("<Faktura>przykładowa treść faktury FA(3)</Faktura>");

        var ciphertext = KsefCryptography.EncryptAes256Cbc(plaintext, key, iv);
        var decrypted = KsefCryptography.DecryptAes256Cbc(ciphertext, key, iv);

        Assert.NotEqual(plaintext, ciphertext);
        Assert.Equal(plaintext, decrypted);
        // PKCS#7: ciphertext must be a whole number of 16-byte blocks, longer than plaintext.
        Assert.Equal(0, ciphertext.Length % 16);
        Assert.True(ciphertext.Length > plaintext.Length);
    }

    [Fact]
    public void EncryptRsaOaepSha256_ProducesPayloadDecryptableWithPrivateKey()
    {
        using var rsa = RSA.Create(2048);
        using var certificate = CreateSelfSignedCertificate(rsa);
        var secret = KsefCryptography.GenerateAes256Key();

        var encrypted = KsefCryptography.EncryptRsaOaepSha256(secret, certificate);
        var decrypted = rsa.Decrypt(encrypted, RSAEncryptionPadding.OaepSHA256);

        Assert.Equal(secret, decrypted);
    }

    [Fact]
    public void EncryptKsefToken_EncryptsTokenPipeTimestamp()
    {
        using var rsa = RSA.Create(2048);
        using var certificate = CreateSelfSignedCertificate(rsa);

        var encryptedBase64 = KsefCryptography.EncryptKsefToken("my-token", 1722500000000, certificate);
        var decrypted = Encoding.UTF8.GetString(
            rsa.Decrypt(Convert.FromBase64String(encryptedBase64), RSAEncryptionPadding.OaepSHA256));

        Assert.Equal("my-token|1722500000000", decrypted);
    }

    [Fact]
    public void Sha256Base64_MatchesKnownVector()
    {
        // SHA-256("abc") = ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0= (base64)
        Assert.Equal("ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=",
            KsefCryptography.Sha256Base64(Encoding.UTF8.GetBytes("abc")));
    }

    private static X509Certificate2 CreateSelfSignedCertificate(RSA rsa)
    {
        var request = new CertificateRequest("CN=KSeF Test", rsa, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
}
