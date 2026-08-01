using ITBees.Ksef.Configuration;
using ITBees.Ksef.Http;
using ITBees.Ksef.Models;
using ITBees.Ksef.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ITBees.Ksef.Auth;

public class KsefAuthenticationService : IKsefAuthenticationService
{
    private static readonly TimeSpan ExpirySafetyMargin = TimeSpan.FromSeconds(60);

    private readonly IKsefApiClient _api;
    private readonly KsefOptions _options;
    private readonly ILogger<KsefAuthenticationService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private TokenInfo? _accessToken;
    private TokenInfo? _refreshToken;

    public KsefAuthenticationService(IKsefApiClient api, IOptions<KsefOptions> options,
        ILogger<KsefAuthenticationService> logger)
    {
        _api = api;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (IsValid(_accessToken))
            return _accessToken!.Token;

        await _lock.WaitAsync(ct);
        try
        {
            if (IsValid(_accessToken))
                return _accessToken!.Token;

            if (IsValid(_refreshToken))
            {
                try
                {
                    var refreshed = await _api.RefreshAccessTokenAsync(_refreshToken!.Token, ct);
                    _accessToken = refreshed.AccessToken;
                    return _accessToken.Token;
                }
                catch (KsefApiException ex)
                {
                    _logger.LogWarning(ex, "KSeF access token refresh failed, falling back to full authentication.");
                }
            }

            await AuthenticateAsync(ct);
            return _accessToken!.Token;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void InvalidateCache()
    {
        _accessToken = null;
        _refreshToken = null;
    }

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.KsefToken))
            throw new InvalidOperationException("KsefOptions.KsefToken is not configured.");
        if (string.IsNullOrWhiteSpace(_options.Nip))
            throw new InvalidOperationException("KsefOptions.Nip is not configured.");

        var challenge = await _api.GetAuthChallengeAsync(ct);

        var certificates = await _api.GetPublicKeyCertificatesAsync(ct);
        var tokenCertificateInfo = SelectCertificate(certificates, PublicKeyCertificateUsage.KsefTokenEncryption);
        using var tokenCertificate = KsefCryptography.LoadCertificate(tokenCertificateInfo.Certificate);

        var request = new InitTokenAuthenticationRequest
        {
            Challenge = challenge.Challenge,
            ContextIdentifier = new ContextIdentifier { Type = "Nip", Value = _options.Nip },
            EncryptedToken = KsefCryptography.EncryptKsefToken(_options.KsefToken, challenge.TimestampMs,
                tokenCertificate),
            PublicKeyId = string.IsNullOrEmpty(tokenCertificateInfo.PublicKeyId)
                ? null
                : tokenCertificateInfo.PublicKeyId
        };

        var init = await _api.SubmitKsefTokenAuthenticationAsync(request, ct);
        await WaitForAuthenticationAsync(init, ct);

        var tokens = await _api.RedeemTokenAsync(init.AuthenticationToken.Token, ct);
        _accessToken = tokens.AccessToken;
        _refreshToken = tokens.RefreshToken;
        _logger.LogInformation("Authenticated in KSeF for NIP {Nip}; access token valid until {ValidUntil}.",
            _options.Nip, _accessToken.ValidUntil);
    }

    private async Task WaitForAuthenticationAsync(AuthenticationInitResponse init, CancellationToken ct)
    {
        for (var attempt = 0; attempt < _options.StatusPollMaxAttempts; attempt++)
        {
            var status = await _api.GetAuthenticationStatusAsync(init.ReferenceNumber,
                init.AuthenticationToken.Token, ct);

            if (status.Status.Code == 200)
                return;
            if (status.Status.Code >= 400)
                throw new KsefApiException($"KSeF authentication failed: {status.Status}",
                    ksefStatusCode: status.Status.Code);

            await Task.Delay(_options.StatusPollDelayMs, ct);
        }

        throw new KsefApiException(
            $"KSeF authentication did not complete within {_options.StatusPollMaxAttempts} status checks.");
    }

    internal static PublicKeyCertificate SelectCertificate(IReadOnlyList<PublicKeyCertificate> certificates,
        string usage)
    {
        var now = DateTimeOffset.UtcNow;
        return certificates
                   .Where(c => c.Usage.Contains(usage) && c.ValidFrom <= now && c.ValidTo >= now)
                   .OrderByDescending(c => c.ValidTo)
                   .FirstOrDefault()
               ?? throw new KsefApiException($"No valid KSeF public key certificate found for usage '{usage}'.");
    }

    private static bool IsValid(TokenInfo? token) =>
        token != null && token.ValidUntil - ExpirySafetyMargin > DateTimeOffset.UtcNow;
}
