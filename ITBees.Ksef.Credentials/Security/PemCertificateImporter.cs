using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ITBees.Ksef.Credentials.Security;

/// <summary>
/// Turns the PEM pair produced by the KSeF certificate wizard (a .crt certificate and an
/// encrypted .key private key) into the PKCS#12 container the credential store keeps.
/// Users can upload the files exactly as KSeF hands them out — no OpenSSL round-trip needed.
/// </summary>
public static class PemCertificateImporter
{
    private const string CertificateHeader = "-----BEGIN CERTIFICATE-----";
    private const string EncryptedKeyHeader = "-----BEGIN ENCRYPTED PRIVATE KEY-----";
    private const string LegacyOpensslMarker = "Proc-Type: 4,ENCRYPTED";

    /// <summary>True when the bytes are a PEM text block rather than a binary PKCS#12 container.</summary>
    public static bool LooksLikePem(byte[] raw) => DecodeText(raw).Contains("-----BEGIN", StringComparison.Ordinal);

    public static bool ContainsCertificate(byte[] raw) =>
        DecodeText(raw).Contains(CertificateHeader, StringComparison.Ordinal);

    public static bool ContainsPrivateKey(byte[] raw) =>
        DecodeText(raw).Contains("PRIVATE KEY-----", StringComparison.Ordinal);

    /// <summary>
    /// Combines a PEM certificate with its PEM private key and exports the result as PKCS#12
    /// protected by <paramref name="password"/> (the same password that decrypts the key, so the
    /// caller keeps storing a single password). Errors surface as <see cref="ArgumentException"/>
    /// with a message the UI can show verbatim.
    /// </summary>
    public static byte[] ToPkcs12(byte[] certificatePemBytes, byte[] privateKeyPemBytes, string? password)
    {
        var certPem = DecodeText(certificatePemBytes);
        var keyPem = DecodeText(privateKeyPemBytes);

        // Users pick the two files in one dialog — catching a swap here beats a cryptic parser error.
        if (!certPem.Contains(CertificateHeader, StringComparison.Ordinal))
            throw new ArgumentException(
                "Wgrany plik certyfikatu nie zawiera bloku „BEGIN CERTIFICATE” — wybierz plik .crt/.pem pobrany z KSeF.");

        if (!keyPem.Contains("PRIVATE KEY-----", StringComparison.Ordinal))
            throw new ArgumentException(
                "Wgrany plik klucza nie zawiera bloku „PRIVATE KEY” — wybierz plik .key pobrany z KSeF.");

        if (keyPem.Contains(LegacyOpensslMarker, StringComparison.Ordinal))
            throw new ArgumentException(
                "Klucz jest zaszyfrowany w starym formacie OpenSSL, którego nie obsługujemy — " +
                "przekonwertuj go do PKCS#8 (openssl pkcs8 -topk8) albo wygeneruj certyfikat w KSeF ponownie.");

        var keyIsEncrypted = keyPem.Contains(EncryptedKeyHeader, StringComparison.Ordinal);
        if (keyIsEncrypted && string.IsNullOrEmpty(password))
            throw new ArgumentException("Klucz prywatny jest zaszyfrowany — podaj hasło do certyfikatu.");

        try
        {
            using var certificate = keyIsEncrypted
                ? X509Certificate2.CreateFromEncryptedPem(certPem, keyPem, password)
                : X509Certificate2.CreateFromPem(certPem, keyPem);

            return certificate.Export(X509ContentType.Pkcs12, password);
        }
        catch (CryptographicException ex)
        {
            throw new ArgumentException(
                "Nie udało się połączyć certyfikatu z kluczem prywatnym — sprawdź hasło i czy oba pliki " +
                "pochodzą z tej samej pary wygenerowanej w KSeF. " + ex.Message);
        }
    }

    /// <summary>PEM is ASCII by definition; the BOM guard covers files re-saved by Windows editors.</summary>
    private static string DecodeText(byte[] raw)
    {
        return Encoding.UTF8.GetString(raw).TrimStart('\uFEFF');
    }
}
