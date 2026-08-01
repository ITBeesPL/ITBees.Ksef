namespace ITBees.Ksef.Auth;

/// <summary>
/// Manages the KSeF API 2.0 token lifecycle: challenge → encrypted KSeF token → JWT access/refresh tokens.
/// Caches the access token and transparently refreshes or re-authenticates when it expires.
/// </summary>
public interface IKsefAuthenticationService
{
    /// <summary>Returns a valid access token for the configured NIP context, authenticating if necessary.</summary>
    Task<string> GetAccessTokenAsync(CancellationToken ct = default);

    /// <summary>Drops cached tokens so the next call performs a full authentication.</summary>
    void InvalidateCache();
}
