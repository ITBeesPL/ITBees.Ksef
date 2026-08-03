using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ITBees.Ksef.Credentials.Security;

/// <summary>
/// AES-256-GCM. Stored format: <c>v1:{base64(nonce | tag | ciphertext)}</c> — the version prefix
/// leaves room to swap the algorithm later without migrating data, because old rows stay recognisable.
/// </summary>
public class AesSecretProtector : ISecretProtector
{
    private const string Version = "v1";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public AesSecretProtector(IOptions<KsefCredentialsOptions> options)
        : this(options.Value.EncryptionKey)
    {
    }

    /// <param name="base64Key">32 bytes (AES-256) encoded as Base64.</param>
    public AesSecretProtector(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
            throw new InvalidOperationException(
                $"{nameof(KsefCredentialsOptions)}.{nameof(KsefCredentialsOptions.EncryptionKey)} is not set — " +
                "KSeF credentials cannot be stored without an encryption key (32 bytes, Base64).");

        _key = DecodeKey(base64Key);
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(_key, TagSize))
        {
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        }

        var payload = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, payload, NonceSize + TagSize, ciphertext.Length);

        return $"{Version}:{Convert.ToBase64String(payload)}";
    }

    public string Unprotect(string protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
            throw new ArgumentException("Encrypted value is empty.", nameof(protectedValue));

        var separator = protectedValue.IndexOf(':');
        if (separator < 0 || protectedValue[..separator] != Version)
            throw new InvalidOperationException("Unknown encrypted value format.");

        var payload = Convert.FromBase64String(protectedValue[(separator + 1)..]);
        if (payload.Length < NonceSize + TagSize)
            throw new InvalidOperationException("Encrypted value is truncated.");

        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var ciphertext = payload.AsSpan(NonceSize + TagSize);
        var plaintext = new byte[ciphertext.Length];

        using (var aes = new AesGcm(_key, TagSize))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }

        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] DecodeKey(string configured)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(configured);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                $"{nameof(KsefCredentialsOptions)}.{nameof(KsefCredentialsOptions.EncryptionKey)} must be Base64 encoded.");
        }

        if (key.Length != 32)
            throw new InvalidOperationException(
                $"{nameof(KsefCredentialsOptions)}.{nameof(KsefCredentialsOptions.EncryptionKey)} must be 32 bytes " +
                $"(AES-256) but is {key.Length}.");

        return key;
    }
}
