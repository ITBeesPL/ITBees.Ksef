namespace ITBees.Ksef.Configuration;

/// <summary>
/// Configuration of the KSeF integration. Bind from configuration section "Ksef".
/// </summary>
public class KsefOptions
{
    public KsefEnvironment Environment { get; set; } = KsefEnvironment.Test;

    /// <summary>Optional explicit base URL override (e.g. "https://api-test.ksef.mf.gov.pl/v2"). When empty, resolved from <see cref="Environment"/>.</summary>
    public string? BaseUrl { get; set; }

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
