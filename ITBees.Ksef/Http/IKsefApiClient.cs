using ITBees.Ksef.Models;

namespace ITBees.Ksef.Http;

/// <summary>
/// Low-level, 1:1 wrapper over the KSeF API 2.0 REST endpoints.
/// Tokens are passed explicitly; lifecycle management lives in <see cref="Auth.IKsefAuthenticationService"/>.
/// </summary>
public interface IKsefApiClient
{
    // -- auth --
    Task<AuthenticationChallengeResponse> GetAuthChallengeAsync(CancellationToken ct = default);

    Task<AuthenticationInitResponse> SubmitKsefTokenAuthenticationAsync(InitTokenAuthenticationRequest request,
        CancellationToken ct = default);

    Task<AuthenticationOperationStatusResponse> GetAuthenticationStatusAsync(string referenceNumber,
        string authenticationToken, CancellationToken ct = default);

    Task<AuthenticationTokensResponse> RedeemTokenAsync(string authenticationToken, CancellationToken ct = default);

    Task<AuthenticationTokenRefreshResponse> RefreshAccessTokenAsync(string refreshToken,
        CancellationToken ct = default);

    // -- security --
    Task<IReadOnlyList<PublicKeyCertificate>> GetPublicKeyCertificatesAsync(CancellationToken ct = default);

    // -- online session --
    Task<OpenOnlineSessionResponse> OpenOnlineSessionAsync(OpenOnlineSessionRequest request, string accessToken,
        CancellationToken ct = default);

    Task<SendInvoiceResponse> SendInvoiceAsync(string sessionReferenceNumber, SendInvoiceRequest request,
        string accessToken, CancellationToken ct = default);

    Task CloseOnlineSessionAsync(string sessionReferenceNumber, string accessToken, CancellationToken ct = default);

    // -- session status / UPO --
    Task<SessionStatusResponse> GetSessionStatusAsync(string sessionReferenceNumber, string accessToken,
        CancellationToken ct = default);

    Task<SessionInvoiceStatusResponse> GetSessionInvoiceStatusAsync(string sessionReferenceNumber,
        string invoiceReferenceNumber, string accessToken, CancellationToken ct = default);

    /// <summary>Downloads the UPO (XML) for a single invoice identified by its KSeF number.</summary>
    Task<string> GetInvoiceUpoAsync(string sessionReferenceNumber, string ksefNumber, string accessToken,
        CancellationToken ct = default);

    /// <summary>Downloads the collective session UPO (XML) generated after the session is closed.</summary>
    Task<string> GetSessionUpoAsync(string sessionReferenceNumber, string upoReferenceNumber, string accessToken,
        CancellationToken ct = default);
}
