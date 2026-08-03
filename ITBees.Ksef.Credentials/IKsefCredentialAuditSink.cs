using ITBees.Ksef.Configuration;

namespace ITBees.Ksef.Credentials;

/// <summary>
/// Receives credential changes so the host can write them to its own audit trail.
/// Optional — when the host registers nothing, <see cref="NullKsefCredentialAuditSink"/> is used.
/// </summary>
public interface IKsefCredentialAuditSink
{
    void Created(Guid companyGuid, KsefCredentialAuditView credential);

    void Updated(Guid companyGuid, KsefCredentialAuditView before, KsefCredentialAuditView after);

    void Deleted(Guid companyGuid, KsefCredentialAuditView credential);

    void ConnectionTested(Guid companyGuid, KsefCredentialAuditView credential, bool success, string? error);
}

/// <summary>
/// What the audit trail gets to see. This is a projection rather than the entity itself so that
/// a secret cannot reach an audit log even if the host serialises everything it is handed.
/// </summary>
public sealed class KsefCredentialAuditView
{
    public Guid Guid { get; init; }

    public KsefCredentialKind Kind { get; init; }

    public KsefEnvironment Environment { get; init; }

    public string Nip { get; init; } = string.Empty;

    /// <summary>Whether a token is stored — the fact, never the value.</summary>
    public bool HasToken { get; init; }

    /// <summary>Whether a certificate is stored — the fact, never the value.</summary>
    public bool HasCertificate { get; init; }

    public string? CertificateFileName { get; init; }
    public string? CertificateSubject { get; init; }
    public string? CertificateThumbprint { get; init; }
    public DateTime? CertificateValidFrom { get; init; }
    public DateTime? CertificateValidTo { get; init; }

    public bool VerifyCertificateChain { get; init; }

    public DateTime? LastVerifiedAt { get; init; }

    public static KsefCredentialAuditView From(KsefCredential credential) => new()
    {
        Guid = credential.Guid,
        Kind = credential.Kind,
        Environment = credential.Environment,
        Nip = credential.Nip,
        HasToken = !string.IsNullOrEmpty(credential.EncryptedToken),
        HasCertificate = !string.IsNullOrEmpty(credential.EncryptedCertificate),
        CertificateFileName = credential.CertificateFileName,
        CertificateSubject = credential.CertificateSubject,
        CertificateThumbprint = credential.CertificateThumbprint,
        CertificateValidFrom = credential.CertificateValidFrom,
        CertificateValidTo = credential.CertificateValidTo,
        VerifyCertificateChain = credential.VerifyCertificateChain,
        LastVerifiedAt = credential.LastVerifiedAt
    };

    /// <summary>
    /// Label identifying the record without opening it. Deliberately a method rather than a property,
    /// so hosts that snapshot properties for a field-level diff do not pick it up as a changed field.
    /// </summary>
    public override string ToString() =>
        Kind == KsefCredentialKind.Certificate
            ? $"Certyfikat {CertificateFileName ?? CertificateSubject} ({Environment}, NIP {Nip})"
            : $"Token ({Environment}, NIP {Nip})";
}

/// <summary>Discards everything — the default when the host does not audit credential changes.</summary>
public sealed class NullKsefCredentialAuditSink : IKsefCredentialAuditSink
{
    public void Created(Guid companyGuid, KsefCredentialAuditView credential)
    {
    }

    public void Updated(Guid companyGuid, KsefCredentialAuditView before, KsefCredentialAuditView after)
    {
    }

    public void Deleted(Guid companyGuid, KsefCredentialAuditView credential)
    {
    }

    public void ConnectionTested(Guid companyGuid, KsefCredentialAuditView credential, bool success, string? error)
    {
    }
}
