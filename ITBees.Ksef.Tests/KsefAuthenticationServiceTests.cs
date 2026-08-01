using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using ITBees.Ksef.Auth;
using ITBees.Ksef.Configuration;
using ITBees.Ksef.Http;
using ITBees.Ksef.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ITBees.Ksef.Tests;

public class KsefAuthenticationServiceTests
{
    [Fact]
    public async Task GetAccessTokenAsync_RunsFullTokenAuthenticationFlow()
    {
        using var rsa = RSA.Create(2048);
        var handler = new FakeKsefHandler(rsa);
        var client = new KsefApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://ksef.local/v2/") });
        var service = new KsefAuthenticationService(client, Options.Create(new KsefOptions
        {
            KsefToken = "secret-ksef-token",
            Nip = "5555555555",
            StatusPollDelayMs = 1
        }), NullLogger<KsefAuthenticationService>.Instance);

        var accessToken = await service.GetAccessTokenAsync();

        Assert.Equal("access-jwt", accessToken);
        // Verify the API received the KSeF token encrypted as "{token}|{timestampMs}".
        Assert.Equal("secret-ksef-token|1722500000000", handler.DecryptedTokenPayload);
        Assert.Equal(new[] { "auth/challenge", "security/public-key-certificates", "auth/ksef-token", "auth/REF-1", "auth/token/redeem" },
            handler.RequestedPaths);

        // Second call should be served from cache — no additional HTTP traffic.
        var second = await service.GetAccessTokenAsync();
        Assert.Equal("access-jwt", second);
        Assert.Equal(5, handler.RequestedPaths.Count);
    }

    private sealed class FakeKsefHandler : HttpMessageHandler
    {
        private readonly RSA _rsa;
        public List<string> RequestedPaths { get; } = new();
        public string? DecryptedTokenPayload { get; private set; }

        public FakeKsefHandler(RSA rsa) => _rsa = rsa;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/').Replace("v2/", "");
            RequestedPaths.Add(path);

            switch (path)
            {
                case "auth/challenge":
                    return Json(new
                    {
                        challenge = "challenge-1",
                        timestamp = "2024-08-01T08:53:20+00:00",
                        timestampMs = 1722500000000,
                        clientIp = "127.0.0.1"
                    });
                case "security/public-key-certificates":
                    var certificateRequest = new CertificateRequest("CN=KSeF", _rsa, HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pkcs1);
                    using (var certificate = certificateRequest.CreateSelfSigned(
                               DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)))
                    {
                        return Json(new[]
                        {
                            new
                            {
                                certificate = Convert.ToBase64String(certificate.Export(X509ContentType.Cert)),
                                certificateId = "cert-1",
                                publicKeyId = "key-1",
                                validFrom = DateTimeOffset.UtcNow.AddDays(-1),
                                validTo = DateTimeOffset.UtcNow.AddDays(1),
                                usage = new[] { "KsefTokenEncryption", "SymmetricKeyEncryption" }
                            }
                        });
                    }
                case "auth/ksef-token":
                    var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                    var encrypted = body.RootElement.GetProperty("encryptedToken").GetString()!;
                    DecryptedTokenPayload = Encoding.UTF8.GetString(
                        _rsa.Decrypt(Convert.FromBase64String(encrypted), RSAEncryptionPadding.OaepSHA256));
                    Assert.Equal("Nip",
                        body.RootElement.GetProperty("contextIdentifier").GetProperty("type").GetString());
                    return Json(new
                    {
                        referenceNumber = "REF-1",
                        authenticationToken = new { token = "auth-jwt", validUntil = DateTimeOffset.UtcNow.AddMinutes(10) }
                    }, HttpStatusCode.Accepted);
                case "auth/REF-1":
                    AssertBearer(request, "auth-jwt");
                    return Json(new
                    {
                        startDate = DateTimeOffset.UtcNow,
                        status = new { code = 200, description = "OK" }
                    });
                case "auth/token/redeem":
                    AssertBearer(request, "auth-jwt");
                    return Json(new
                    {
                        accessToken = new { token = "access-jwt", validUntil = DateTimeOffset.UtcNow.AddMinutes(15) },
                        refreshToken = new { token = "refresh-jwt", validUntil = DateTimeOffset.UtcNow.AddDays(7) }
                    });
                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound)
                    {
                        Content = new StringContent($"Unexpected path {path}")
                    };
            }
        }

        private static void AssertBearer(HttpRequestMessage request, string expectedToken) =>
            Assert.Equal(expectedToken, request.Headers.Authorization?.Parameter);

        private static HttpResponseMessage Json(object payload, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            var content = new StringContent(JsonSerializer.Serialize(payload, KsefApiClient.JsonOptions),
                Encoding.UTF8);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return new HttpResponseMessage(statusCode) { Content = content };
        }
    }
}
