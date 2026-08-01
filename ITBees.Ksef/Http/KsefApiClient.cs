using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ITBees.Ksef.Models;

namespace ITBees.Ksef.Http;

public class KsefApiClient : IKsefApiClient
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public KsefApiClient(HttpClient http)
    {
        _http = http;
    }

    public Task<AuthenticationChallengeResponse> GetAuthChallengeAsync(CancellationToken ct = default) =>
        PostAsync<AuthenticationChallengeResponse>("auth/challenge", content: null, accessToken: null, ct);

    public Task<AuthenticationInitResponse> SubmitKsefTokenAuthenticationAsync(
        InitTokenAuthenticationRequest request, CancellationToken ct = default) =>
        PostAsync<AuthenticationInitResponse>("auth/ksef-token", request, accessToken: null, ct);

    public Task<AuthenticationOperationStatusResponse> GetAuthenticationStatusAsync(string referenceNumber,
        string authenticationToken, CancellationToken ct = default) =>
        GetAsync<AuthenticationOperationStatusResponse>($"auth/{Uri.EscapeDataString(referenceNumber)}",
            authenticationToken, ct);

    public Task<AuthenticationTokensResponse> RedeemTokenAsync(string authenticationToken,
        CancellationToken ct = default) =>
        PostAsync<AuthenticationTokensResponse>("auth/token/redeem", content: null, authenticationToken, ct);

    public Task<AuthenticationTokenRefreshResponse> RefreshAccessTokenAsync(string refreshToken,
        CancellationToken ct = default) =>
        PostAsync<AuthenticationTokenRefreshResponse>("auth/token/refresh", content: null, refreshToken, ct);

    public async Task<IReadOnlyList<PublicKeyCertificate>> GetPublicKeyCertificatesAsync(
        CancellationToken ct = default) =>
        await GetAsync<List<PublicKeyCertificate>>("security/public-key-certificates", accessToken: null, ct);

    public Task<OpenOnlineSessionResponse> OpenOnlineSessionAsync(OpenOnlineSessionRequest request,
        string accessToken, CancellationToken ct = default) =>
        PostAsync<OpenOnlineSessionResponse>("sessions/online", request, accessToken, ct);

    public Task<SendInvoiceResponse> SendInvoiceAsync(string sessionReferenceNumber, SendInvoiceRequest request,
        string accessToken, CancellationToken ct = default) =>
        PostAsync<SendInvoiceResponse>($"sessions/online/{Uri.EscapeDataString(sessionReferenceNumber)}/invoices",
            request, accessToken, ct);

    public async Task CloseOnlineSessionAsync(string sessionReferenceNumber, string accessToken,
        CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Post,
            $"sessions/online/{Uri.EscapeDataString(sessionReferenceNumber)}/close", content: null, accessToken);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public Task<SessionStatusResponse> GetSessionStatusAsync(string sessionReferenceNumber, string accessToken,
        CancellationToken ct = default) =>
        GetAsync<SessionStatusResponse>($"sessions/{Uri.EscapeDataString(sessionReferenceNumber)}", accessToken, ct);

    public Task<SessionInvoiceStatusResponse> GetSessionInvoiceStatusAsync(string sessionReferenceNumber,
        string invoiceReferenceNumber, string accessToken, CancellationToken ct = default) =>
        GetAsync<SessionInvoiceStatusResponse>(
            $"sessions/{Uri.EscapeDataString(sessionReferenceNumber)}/invoices/{Uri.EscapeDataString(invoiceReferenceNumber)}",
            accessToken, ct);

    public Task<string> GetInvoiceUpoAsync(string sessionReferenceNumber, string ksefNumber, string accessToken,
        CancellationToken ct = default) =>
        GetStringAsync(
            $"sessions/{Uri.EscapeDataString(sessionReferenceNumber)}/invoices/ksef/{Uri.EscapeDataString(ksefNumber)}/upo",
            accessToken, ct);

    public Task<string> GetSessionUpoAsync(string sessionReferenceNumber, string upoReferenceNumber,
        string accessToken, CancellationToken ct = default) =>
        GetStringAsync(
            $"sessions/{Uri.EscapeDataString(sessionReferenceNumber)}/upo/{Uri.EscapeDataString(upoReferenceNumber)}",
            accessToken, ct);

    private async Task<T> GetAsync<T>(string url, string? accessToken, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, url, content: null, accessToken);
        using var response = await _http.SendAsync(request, ct);
        return await ReadAsync<T>(response, ct);
    }

    private async Task<string> GetStringAsync(string url, string? accessToken, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, url, content: null, accessToken);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<T> PostAsync<T>(string url, object? content, string? accessToken, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Post, url, content, accessToken);
        using var response = await _http.SendAsync(request, ct);
        return await ReadAsync<T>(response, ct);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, object? content,
        string? accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        if (accessToken != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (content != null)
            request.Content = JsonContent.Create(content, options: JsonOptions);
        return request;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        await EnsureSuccessAsync(response, ct);
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        return result ?? throw new KsefApiException(
            $"KSeF API returned an empty body for {response.RequestMessage?.RequestUri}.",
            response.StatusCode);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new KsefApiException(
            $"KSeF API call {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} failed with HTTP {(int)response.StatusCode}: {Truncate(body)}",
            response.StatusCode, body);
    }

    private static string Truncate(string value) => value.Length <= 2000 ? value : value[..2000] + "...";
}
