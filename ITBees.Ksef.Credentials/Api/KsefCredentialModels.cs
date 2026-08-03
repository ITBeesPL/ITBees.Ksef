using ITBees.Ksef.Configuration;

namespace ITBees.Ksef.Credentials.Api;

/// <summary>State of the KSeF integration as shown in the UI. Never carries a secret.</summary>
public class KsefCredentialVm
{
    public bool Configured { get; set; }

    public KsefCredentialKind Kind { get; set; }

    public KsefEnvironment Environment { get; set; }

    public string Nip { get; set; } = string.Empty;

    /// <summary>Masked token, e.g. "KSEF-7A8B…9M0N". Never the full value.</summary>
    public string? MaskedToken { get; set; }

    public string? CertificateFileName { get; set; }
    public string? CertificateSubject { get; set; }
    public string? CertificateThumbprint { get; set; }
    public DateTime? CertificateValidFrom { get; set; }
    public DateTime? CertificateValidTo { get; set; }

    /// <summary>Certificate is past its validity — the UI shows a warning instead of a green status.</summary>
    public bool CertificateExpired { get; set; }

    public DateTime? LastVerifiedAt { get; set; }
    public string? LastError { get; set; }

    public DateTime? Created { get; set; }
    public DateTime? Updated { get; set; }
}

/// <summary>
/// Saves a credential. Depending on <see cref="Kind"/> fill in <see cref="Token"/>
/// or <see cref="CertificateBase64"/> — fields of the other kind are ignored.
/// </summary>
public class KsefCredentialIm
{
    public KsefCredentialKind Kind { get; set; }

    public KsefEnvironment Environment { get; set; } = KsefEnvironment.Test;

    /// <summary>Context NIP; empty falls back to the company's registration data.</summary>
    public string? Nip { get; set; }

    /// <summary>Token generated in the KSeF web application.</summary>
    public string? Token { get; set; }

    /// <summary>Contents of the .p12/.pfx file, Base64 encoded.</summary>
    public string? CertificateBase64 { get; set; }

    /// <summary>Password of the PKCS#12 container.</summary>
    public string? CertificatePassword { get; set; }

    /// <summary>Name of the uploaded file — display only.</summary>
    public string? CertificateFileName { get; set; }

    /// <summary>Turn off for self-signed certificates (accepted by the TEST environment only).</summary>
    public bool VerifyCertificateChain { get; set; } = true;
}

/// <summary>Result of a KSeF connection test.</summary>
public class KsefConnectionTestVm
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public DateTime CheckedAt { get; set; }
}
