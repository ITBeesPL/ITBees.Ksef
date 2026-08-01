namespace ITBees.Ksef.Configuration;

/// <summary>
/// KSeF API 2.0 environments as published by the Ministry of Finance.
/// </summary>
public enum KsefEnvironment
{
    /// <summary>https://api-test.ksef.mf.gov.pl/v2 — developer testing, self-signed certificates allowed.</summary>
    Test,

    /// <summary>https://api-demo.ksef.mf.gov.pl/v2 — pre-production, mirrors production configuration.</summary>
    Demo,

    /// <summary>https://api.ksef.mf.gov.pl/v2 — production, invoices have full legal validity.</summary>
    Production
}
