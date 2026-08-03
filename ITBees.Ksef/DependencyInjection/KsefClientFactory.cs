using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ITBees.Ksef.Auth;
using ITBees.Ksef.Configuration;
using ITBees.Ksef.Http;
using ITBees.Ksef.Invoicing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ITBees.Ksef.DependencyInjection;

public class KsefClientFactory : IKsefClientFactory
{
    /// <summary>Named HttpClient without a base address — each tenant gets its own environment URL.</summary>
    public const string HttpClientName = "ITBees.Ksef.Tenant";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IFa3XmlGenerator _xmlGenerator;

    /// <summary>
    /// KSeF sessions are expensive (challenge → sign → poll → redeem), so the authentication
    /// service — which owns the access/refresh token cache — is kept per credential.
    /// </summary>
    private readonly ConcurrentDictionary<string, IKsefAuthenticationService> _authentications = new();

    public KsefClientFactory(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory,
        IFa3XmlGenerator xmlGenerator)
    {
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _xmlGenerator = xmlGenerator;
    }

    public IKsefInvoiceService CreateInvoiceService(KsefOptions options) =>
        new KsefInvoiceService(CreateApiClient(options), GetAuthentication(options), _xmlGenerator,
            Options.Create(options), _loggerFactory.CreateLogger<KsefInvoiceService>());

    public IKsefInvoiceQueryService CreateQueryService(KsefOptions options) =>
        new KsefInvoiceQueryService(CreateApiClient(options), GetAuthentication(options),
            _loggerFactory.CreateLogger<KsefInvoiceQueryService>());

    public void InvalidateAuthentication(KsefOptions options)
    {
        if (_authentications.TryRemove(BuildCacheKey(options), out var authentication))
            authentication.InvalidateCache();
    }

    private IKsefApiClient CreateApiClient(KsefOptions options)
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);
        http.BaseAddress = new Uri(options.GetBaseUrl() + "/");
        http.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
        return new KsefApiClient(http);
    }

    private IKsefAuthenticationService GetAuthentication(KsefOptions options) =>
        _authentications.GetOrAdd(BuildCacheKey(options),
            _ => new KsefAuthenticationService(CreateApiClient(options), Options.Create(options),
                _loggerFactory.CreateLogger<KsefAuthenticationService>()));

    /// <summary>
    /// Identifies a credential without keeping the secret itself in a dictionary key —
    /// changing the token or the certificate produces a different key, which forces a re-login.
    /// </summary>
    private static string BuildCacheKey(KsefOptions options)
    {
        var secret = options.AuthMode == KsefAuthMode.Certificate
            ? (options.Certificate?.Pkcs12Base64 ?? options.Certificate?.Pkcs12Path ?? string.Empty)
            : options.KsefToken;

        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)))[..16];
        return $"{options.GetBaseUrl()}|{options.Nip}|{options.AuthMode}|{fingerprint}";
    }
}
