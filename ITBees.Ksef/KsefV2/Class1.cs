using ITBees.Ksef.Core;
using ITBees.Ksef.Transport;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace ITBees.Ksef.KsefV2;

public sealed class KsefClient : IKsefClient
{
    private readonly HttpClient _http;
    private readonly KsefOptions _opt;

    public KsefClient(HttpClient http, IOptions<KsefOptions> opt)
    {
        _http = http; _opt = opt.Value;
    }

    public async Task<string> OpenSessionByTokenAsync(string nip, string token, CancellationToken ct)
    {
        // Shape per TE Swagger: replace with exact properties from /docs v2.
        var req = new { nip, token };
        using var resp = await _http.PostAsJsonAsync(_opt.Endpoints.SessionInitToken, req, ct);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<SessionDto>(cancellationToken: ct)
                  ?? throw new InvalidOperationException("Empty session response");
        return dto.sessionId;
    }

    public async Task<string> SubmitInvoiceAsync(string sessionId, byte[] encryptedPayload, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(sessionId), "sessionId" },
            // In TE: use field names per Swagger, e.g., "file" or "invoice"; filename p7m if CMS used.
            { new ByteArrayContent(encryptedPayload), "file", "invoice.p7m" }
        };
        using var resp = await _http.PostAsync(_opt.Endpoints.InvoicesSubmit, form, ct);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<SubmitDto>(cancellationToken: ct)
                  ?? throw new InvalidOperationException("Empty submit response");
        return dto.referenceId;
    }

    public async Task<StatusDto> GetStatusAsync(string sessionId, string referenceId, CancellationToken ct)
    {
        var url = $"{_opt.Endpoints.InvoicesStatus}?sessionId={Uri.EscapeDataString(sessionId)}&referenceId={Uri.EscapeDataString(referenceId)}";
        using var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<StatusDto>(cancellationToken: ct)
                  ?? throw new InvalidOperationException("Empty status response");
        return dto;
    }

    public async Task<byte[]> DownloadUpoAsync(string sessionId, string referenceId, CancellationToken ct)
    {
        var url = $"{_opt.Endpoints.InvoicesUpo}?sessionId={Uri.EscapeDataString(sessionId)}&referenceId={Uri.EscapeDataString(referenceId)}";
        using var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    private sealed record SessionDto(string sessionId);
    private sealed record SubmitDto(string referenceId);
}

public sealed record StatusDto(string status, string? ksefNumber, string? rejectionReason);