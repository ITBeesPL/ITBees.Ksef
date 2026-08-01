using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ITBees.Ksef.Security;

/// <summary>
/// Cryptographic primitives required by KSeF API 2.0:
/// AES-256-CBC (PKCS#7) for invoice content, RSAES-OAEP (SHA-256/MGF1) for the symmetric key
/// and the KSeF token, SHA-256 hashes transmitted as Base64.
/// </summary>
public static class KsefCryptography
{
    public static byte[] GenerateAes256Key() => RandomNumberGenerator.GetBytes(32);

    public static byte[] GenerateIv() => RandomNumberGenerator.GetBytes(16);

    public static byte[] EncryptAes256Cbc(byte[] plaintext, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
    }

    public static byte[] DecryptAes256Cbc(byte[] ciphertext, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
    }

    public static byte[] EncryptRsaOaepSha256(byte[] data, X509Certificate2 ksefPublicCertificate)
    {
        using var rsa = ksefPublicCertificate.GetRSAPublicKey()
                        ?? throw new InvalidOperationException("KSeF certificate does not contain an RSA public key.");
        return rsa.Encrypt(data, RSAEncryptionPadding.OaepSHA256);
    }

    /// <summary>Builds the encrypted token payload for POST /auth/ksef-token: RSA-OAEP("{token}|{challengeTimestampMs}").</summary>
    public static string EncryptKsefToken(string ksefToken, long challengeTimestampMs,
        X509Certificate2 ksefPublicCertificate)
    {
        var payload = Encoding.UTF8.GetBytes($"{ksefToken}|{challengeTimestampMs}");
        return Convert.ToBase64String(EncryptRsaOaepSha256(payload, ksefPublicCertificate));
    }

    public static string Sha256Base64(byte[] data) => Convert.ToBase64String(SHA256.HashData(data));

    public static X509Certificate2 LoadCertificate(string base64Der) =>
        new(Convert.FromBase64String(base64Der));
}
