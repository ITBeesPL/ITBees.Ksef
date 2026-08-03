namespace ITBees.Ksef.Credentials.Security;

/// <summary>
/// Encrypts the secrets kept in the host database (KSeF token, PKCS#12 container and its password).
/// Access to the database alone must not be enough to impersonate a company in KSeF.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Returns an encrypted, self-describing representation of the text.</summary>
    string Protect(string plaintext);

    /// <summary>Inverse of <see cref="Protect"/>. Throws when the data was tampered with or the key changed.</summary>
    string Unprotect(string protectedValue);
}
