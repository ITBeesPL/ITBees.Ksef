using ITBees.Ksef.Core;

namespace ITBees.Ksef.Security;

public interface ICredentialStore
{
    // przechowuj per wystawca; sugeruję szyfrowanie w DB (np. DPAPI lub Azure Key Vault)
    Task<KsefCredentials> GetAsync(IssuerId issuer, CancellationToken ct);
}