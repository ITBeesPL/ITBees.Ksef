using ITBees.Ksef.Configuration;

namespace ITBees.Ksef.DependencyInjection;

/// <summary>
/// Builds KSeF services for options resolved at runtime instead of from the "Ksef" configuration section.
/// Multi-tenant hosts need this: every company authenticates with its own NIP and its own token/certificate.
/// </summary>
public interface IKsefClientFactory
{
    IKsefInvoiceService CreateInvoiceService(KsefOptions options);

    IKsefInvoiceQueryService CreateQueryService(KsefOptions options);

    /// <summary>
    /// Drops the cached KSeF session for the given options — call it after a company's
    /// credentials change, otherwise the old access token stays valid until it expires.
    /// </summary>
    void InvalidateAuthentication(KsefOptions options);
}
