namespace ITBees.Ksef.Credentials;

/// <summary>
/// Tells the credential store which company the current request belongs to. Implemented by the host,
/// because how a company is resolved (logged-in user, API key, background job) is the host's business.
/// </summary>
/// <remarks>
/// Every query in this library is narrowed to the company returned here and never to an identifier
/// coming from the client — an implementation that trusts request input would let one tenant read
/// another tenant's KSeF credential.
/// </remarks>
public interface IKsefCompanyContext
{
    /// <summary>Company the current request acts on. Throws when there is none.</summary>
    Guid GetCurrentCompanyGuid();

    /// <summary>
    /// NIP from the company's registration data, used as a fallback when the caller saves
    /// a credential without giving one explicitly. Null when the host does not know it.
    /// </summary>
    string? GetCurrentCompanyNip();
}
