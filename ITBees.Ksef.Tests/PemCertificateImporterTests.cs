using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ITBees.Ksef.Credentials.Security;
using Xunit;

namespace ITBees.Ksef.Tests;

/// <summary>
/// The importer's contract: the .crt/.key pair downloaded from the KSeF certificate wizard goes in,
/// a PKCS#12 container protected by the same password comes out.
/// </summary>
public class PemCertificateImporterTests
{
    private const string Password = "correct-horse-battery";

    [Fact]
    public void ToPkcs12_EcPairWithEncryptedKey_RoundTripsWithTheSamePassword()
    {
        var (certPem, keyPem, thumbprint) = CreateEcPair(encryptKey: true);

        var pkcs12 = PemCertificateImporter.ToPkcs12(certPem, keyPem, Password);

        using var reloaded = new X509Certificate2(pkcs12, Password, X509KeyStorageFlags.EphemeralKeySet);
        Assert.True(reloaded.HasPrivateKey);
        Assert.Equal(thumbprint, reloaded.Thumbprint);
        Assert.NotNull(reloaded.GetECDsaPrivateKey());
    }

    [Fact]
    public void ToPkcs12_RsaPairWithPlainKey_WorksWithoutPassword()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Testowa Firma, OID.2.5.4.97=VATPL-5252445761, C=PL",
            rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        var certPem = Encoding.ASCII.GetBytes(certificate.ExportCertificatePem());
        var keyPem = Encoding.ASCII.GetBytes(rsa.ExportPkcs8PrivateKeyPem());

        var pkcs12 = PemCertificateImporter.ToPkcs12(certPem, keyPem, password: null);

        using var reloaded = new X509Certificate2(pkcs12, (string?)null, X509KeyStorageFlags.EphemeralKeySet);
        Assert.True(reloaded.HasPrivateKey);
        Assert.Equal(certificate.Thumbprint, reloaded.Thumbprint);
    }

    [Fact]
    public void ToPkcs12_CombinedPemFile_YieldsCertificateWithKey()
    {
        var (certPem, keyPem, thumbprint) = CreateEcPair(encryptKey: true);
        var combined = Encoding.ASCII.GetBytes(
            Encoding.ASCII.GetString(certPem) + "\n" + Encoding.ASCII.GetString(keyPem));

        var pkcs12 = PemCertificateImporter.ToPkcs12(combined, combined, Password);

        using var reloaded = new X509Certificate2(pkcs12, Password, X509KeyStorageFlags.EphemeralKeySet);
        Assert.True(reloaded.HasPrivateKey);
        Assert.Equal(thumbprint, reloaded.Thumbprint);
    }

    [Fact]
    public void ToPkcs12_EncryptedKeyWithoutPassword_SaysThePasswordIsMissing()
    {
        var (certPem, keyPem, _) = CreateEcPair(encryptKey: true);

        var exception = Assert.Throws<ArgumentException>(() =>
            PemCertificateImporter.ToPkcs12(certPem, keyPem, password: null));

        Assert.Contains("hasło", exception.Message);
    }

    [Fact]
    public void ToPkcs12_WrongPassword_SaysToCheckThePassword()
    {
        var (certPem, keyPem, _) = CreateEcPair(encryptKey: true);

        var exception = Assert.Throws<ArgumentException>(() =>
            PemCertificateImporter.ToPkcs12(certPem, keyPem, "not-the-password"));

        Assert.Contains("hasło", exception.Message);
    }

    [Fact]
    public void ToPkcs12_SwappedFiles_PointsAtTheCertificateSlot()
    {
        var (certPem, keyPem, _) = CreateEcPair(encryptKey: true);

        var exception = Assert.Throws<ArgumentException>(() =>
            PemCertificateImporter.ToPkcs12(keyPem, certPem, Password));

        Assert.Contains("BEGIN CERTIFICATE", exception.Message);
    }

    [Fact]
    public void LooksLikePem_DistinguishesPemFromPkcs12()
    {
        var (certPem, keyPem, _) = CreateEcPair(encryptKey: true);
        var pkcs12 = PemCertificateImporter.ToPkcs12(certPem, keyPem, Password);

        Assert.True(PemCertificateImporter.LooksLikePem(certPem));
        Assert.True(PemCertificateImporter.ContainsPrivateKey(keyPem));
        Assert.False(PemCertificateImporter.ContainsPrivateKey(certPem));
        Assert.False(PemCertificateImporter.LooksLikePem(pkcs12));
    }

    /// <summary>Mirrors what the KSeF wizard hands out: PEM certificate + encrypted PKCS#8 EC key.</summary>
    private static (byte[] CertPem, byte[] KeyPem, string Thumbprint) CreateEcPair(bool encryptKey)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Jan Testowy, OID.2.5.4.97=VATPL-5252445761, C=PL",
            ecdsa, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        var keyPem = encryptKey
            ? ecdsa.ExportEncryptedPkcs8PrivateKeyPem(Password,
                new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000))
            : ecdsa.ExportPkcs8PrivateKeyPem();

        return (Encoding.ASCII.GetBytes(certificate.ExportCertificatePem()),
            Encoding.ASCII.GetBytes(keyPem), certificate.Thumbprint);
    }
}
