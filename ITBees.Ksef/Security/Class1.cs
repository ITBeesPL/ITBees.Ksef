
using ITBees.Ksef.Core;

public sealed class KsefCredentials
{
    public string Token { get; init; } = default!;   // token autoryzacyjny KSeF (per NIP)
    public string? PrivateKeyPem { get; init; }      // jeżeli podpisujesz lokalnie
}

public interface ICredentialStore
{
    // przechowuj per wystawca; sugeruję szyfrowanie w DB (np. DPAPI lub Azure Key Vault)
    Task<KsefCredentials> GetAsync(IssuerId issuer, CancellationToken ct);
}

public interface IKsefEncryptionService
{
    // KSeF 2.0: szyfrowanie danych faktury przed wysyłką (CMS/PKCS#7 z kluczem publicznym KSeF)
    Task<byte[]> EncryptAsync(byte[] xmlBytes, CancellationToken ct);
}