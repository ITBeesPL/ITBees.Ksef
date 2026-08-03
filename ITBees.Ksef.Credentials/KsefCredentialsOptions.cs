namespace ITBees.Ksef.Credentials;

/// <summary>Host-level configuration of the credential store.</summary>
public class KsefCredentialsOptions
{
    /// <summary>
    /// AES-256 key used to encrypt stored secrets: 32 bytes, Base64 encoded. Required.
    /// </summary>
    /// <remarks>
    /// Replacing the key invalidates every stored credential — they have to be entered again.
    /// </remarks>
    public string? EncryptionKey { get; set; }

    /// <summary>
    /// Value of Naglowek/SystemInfo in invoices sent with credentials from this store —
    /// normally the host application's name.
    /// </summary>
    public string SystemInfo { get; set; } = "ITBees.Ksef";
}
