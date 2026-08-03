using System.Security.Cryptography.X509Certificates;

namespace ITBees.Ksef.Configuration;

/// <summary>
/// Configuration of the KSeF integration. Bind from configuration section "Ksef".
/// </summary>
public class KsefOptions
{
    public KsefEnvironment Environment { get; set; } = KsefEnvironment.Test;

    /// <summary>Optional explicit base URL override (e.g. "https://api-test.ksef.mf.gov.pl/v2"). When empty, resolved from <see cref="Environment"/>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Whether to authenticate with a KSeF token or with a certificate (XAdES signature).</summary>
    public KsefAuthMode AuthMode { get; set; } = KsefAuthMode.Token;

    /// <summary>Certificate used when <see cref="AuthMode"/> is <see cref="KsefAuthMode.Certificate"/>.</summary>
    public KsefCertificateOptions? Certificate { get; set; }

    /// <summary>KSeF authorization token generated for the seller's NIP context (in the KSeF web application or via API).</summary>
    public string KsefToken { get; set; } = string.Empty;

    /// <summary>NIP of the authentication context — the seller (invoice issuer).</summary>
    public string Nip { get; set; } = string.Empty;

    /// <summary>Value of Naglowek/SystemInfo in generated FA(3) invoices.</summary>
    public string SystemInfo { get; set; } = "ITBees.Ksef";

    public int HttpTimeoutSeconds { get; set; } = 100;

    /// <summary>Maximum number of status polls while waiting for authentication / invoice processing.</summary>
    public int StatusPollMaxAttempts { get; set; } = 30;

    /// <summary>Delay between status polls, in milliseconds.</summary>
    public int StatusPollDelayMs { get; set; } = 2000;

    /// <summary>Default seller (Podmiot1) data used by the FA(3) generator when the invoice does not provide its own.</summary>
    public KsefSellerOptions? Seller { get; set; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return BaseUrl.TrimEnd('/');

        return Environment switch
        {
            KsefEnvironment.Test => "https://api-test.ksef.mf.gov.pl/v2",
            KsefEnvironment.Demo => "https://api-demo.ksef.mf.gov.pl/v2",
            KsefEnvironment.Production => "https://api.ksef.mf.gov.pl/v2",
            _ => throw new InvalidOperationException($"Unknown KSeF environment: {Environment}")
        };
    }
}

/// <summary>
/// PKCS#12 material for <see cref="KsefAuthMode.Certificate"/>. Exactly one source
/// (<see cref="Pkcs12Base64"/> or <see cref="Pkcs12Path"/>) has to be provided.
/// </summary>
public class KsefCertificateOptions
{
    /// <summary>Base64 encoded .p12/.pfx — the form used when the certificate comes from a database instead of disk.</summary>
    public string? Pkcs12Base64 { get; set; }

    /// <summary>Path to a .p12/.pfx file on disk.</summary>
    public string? Pkcs12Path { get; set; }

    /// <summary>Password protecting the PKCS#12 container; null when it has none.</summary>
    public string? Password { get; set; }

    /// <summary>
    /// Passed to KSeF as ?verifyCertificateChain. Must be false for self-signed certificates,
    /// which the TEST environment accepts.
    /// </summary>
    public bool VerifyCertificateChain { get; set; } = true;

    /// <summary>Loads the certificate together with its private key (required for signing).</summary>
    public X509Certificate2 Load()
    {
        // EphemeralKeySet keeps the private key in memory — a server process must not litter
        // the per-user key store every time it signs a challenge.
        const X509KeyStorageFlags flags = X509KeyStorageFlags.EphemeralKeySet;

        if (!string.IsNullOrWhiteSpace(Pkcs12Base64))
            return new X509Certificate2(Convert.FromBase64String(Pkcs12Base64), Password, flags);

        if (!string.IsNullOrWhiteSpace(Pkcs12Path))
        {
            if (!File.Exists(Pkcs12Path))
                throw new FileNotFoundException($"KSeF certificate file not found: {Pkcs12Path}", Pkcs12Path);
            return new X509Certificate2(File.ReadAllBytes(Pkcs12Path), Password, flags);
        }

        throw new InvalidOperationException(
            "KsefOptions.Certificate requires either Pkcs12Base64 or Pkcs12Path when AuthMode is Certificate.");
    }
}

public class KsefSellerOptions
{
    public string Nip { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Street with building/apartment number, e.g. "ul. Kwiatowa 1 m. 2".</summary>
    public string AddressLine1 { get; set; } = string.Empty;
    /// <summary>Postal code and city, e.g. "00-001 Warszawa".</summary>
    public string AddressLine2 { get; set; } = string.Empty;
    public string CountryCode { get; set; } = "PL";
    public string? Email { get; set; }
    public string? Phone { get; set; }
}
