namespace ITBees.Ksef.Security;

public sealed class KsefCredentials
{
    public string Token { get; init; } = default!;   // token autoryzacyjny KSeF (per NIP)
    public string? PrivateKeyPem { get; init; }      // jeżeli podpisujesz lokalnie
}