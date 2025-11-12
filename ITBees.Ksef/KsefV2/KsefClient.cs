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

    public async Task<string> OpenSessionAsync(string nip, string token, CancellationToken ct)
    {
        var req = new { nip, token }; // dopasuj do Swaggera TE
        using var resp = await _http.PostAsJsonAsync(_opt.Endpoints.SessionInitToken, req, ct);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<SessionDto>(cancellationToken: ct)
                  ?? throw new InvalidOperationException("Empty session response");
        return dto.SessionId;
    }

    public async Task<SubmitResult> SubmitInvoiceAsync(string sessionId, byte[] encryptedPayload, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(sessionId), "sessionId" },
            { new ByteArrayContent(encryptedPayload), "file", "invoice.p7m" }
        };
        using var resp = await _http.PostAsync(_opt.Endpoints.InvoicesSubmit, form, ct);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<SubmitDto>(cancellationToken: ct)
                  ?? throw new InvalidOperationException("Empty submit response");
        return new SubmitResult(dto.ReferenceId);
    }

    public async Task<StatusResult> GetStatusAsync(string sessionId, string referenceId, CancellationToken ct)
    {
        var url = $"{_opt.Endpoints.InvoicesStatus}?sessionId={Uri.EscapeDataString(sessionId)}&referenceId={Uri.EscapeDataString(referenceId)}";
        using var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<StatusDto>(cancellationToken: ct)
                  ?? throw new InvalidOperationException("Empty status response");

        return new StatusResult(dto.Status, dto.KsefNumber, dto.RejectionReason, dto.UpoAvailableAt);
    }

    public async Task<byte[]> DownloadUpoAsync(string sessionId, string referenceId, CancellationToken ct)
    {
        var url = $"{_opt.Endpoints.InvoicesUpo}?sessionId={Uri.EscapeDataString(sessionId)}&referenceId={Uri.EscapeDataString(referenceId)}";
        using var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    // DTOs tylko lokalnie w KsefV2
    private sealed record SessionDto(string SessionId);
    private sealed record SubmitDto(string ReferenceId);
    private sealed record StatusDto(string Status, string? KsefNumber, string? RejectionReason, DateTimeOffset? UpoAvailableAt);
}