using ITBees.Ksef.Configuration;

namespace ITBees.Ksef.Credentials;

/// <summary>How a company authenticates against KSeF.</summary>
public enum KsefCredentialKind
{
    /// <summary>Authorization token generated in the KSeF web application.</summary>
    Token = 0,

    /// <summary>Certificate (.p12/.pfx) — XAdES signature instead of a token.</summary>
    Certificate = 1
}

/// <summary>
/// KSeF credential of a single company. The secret itself (token, or the PKCS#12 container together
/// with its password) is stored encrypted only — see <see cref="Security.ISecretProtector"/>.
/// </summary>
/// <remarks>
/// The entity deliberately has no navigation property to a company: the host owns that type.
/// Add the foreign key from the host's model builder — see
/// <see cref="Setup.KsefCredentialsDbModelBuilder.RegisterDbModels"/>.
/// </remarks>
public class KsefCredential
{
    public Guid Guid { get; set; }

    public Guid CompanyGuid { get; set; }

    public KsefCredentialKind Kind { get; set; }

    public KsefEnvironment Environment { get; set; } = KsefEnvironment.Test;

    /// <summary>
    /// NIP of the authentication context. Usually the company's own, but an accounting office
    /// may act on someone else's.
    /// </summary>
    public string Nip { get; set; } = string.Empty;

    /// <summary>Encrypted KSeF token — set only for <see cref="KsefCredentialKind.Token"/>.</summary>
    public string? EncryptedToken { get; set; }

    /// <summary>Encrypted PKCS#12 container (Base64 encoded before encryption).</summary>
    public string? EncryptedCertificate { get; set; }

    /// <summary>Encrypted password of the PKCS#12 container.</summary>
    public string? EncryptedCertificatePassword { get; set; }

    /// <summary>Name of the file the user uploaded — shown in the settings screen.</summary>
    public string? CertificateFileName { get; set; }

    /// <summary>Certificate subject (CN/O), so the user can recognise it at a glance.</summary>
    public string? CertificateSubject { get; set; }

    /// <summary>SHA-1 thumbprint of the certificate.</summary>
    public string? CertificateThumbprint { get; set; }

    public DateTime? CertificateValidFrom { get; set; }
    public DateTime? CertificateValidTo { get; set; }

    /// <summary>
    /// Turns off certificate chain validation on the KSeF side — required for self-signed
    /// certificates, which only the TEST environment accepts.
    /// </summary>
    public bool VerifyCertificateChain { get; set; } = true;

    /// <summary>Last successful KSeF login with this credential.</summary>
    public DateTime? LastVerifiedAt { get; set; }

    /// <summary>Message of the last connection error; cleared after a successful test.</summary>
    public string? LastError { get; set; }

    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }

    public bool IsCertificateExpired(DateTime utcNow) =>
        CertificateValidTo.HasValue && CertificateValidTo.Value < utcNow;
}
