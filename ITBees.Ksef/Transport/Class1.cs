using ITBees.Ksef.Core;
using ITBees.Ksef.KsefV2;

namespace ITBees.Ksef.Transport;

public sealed class KsefClientOptions
{
    public required Uri BaseUrl { get; init; } // np. https://ksef-test.mf.gov.pl/api
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(100);
}

public sealed record SubmitResult(string ReferenceId);  // identyfikator przesyłki
public sealed record StatusResult(string? KsefNumber, string Status, string? RejectionReason, DateTimeOffset? UpoAvailableAt);

public interface IKsefClient
{
    Task<string> OpenSessionByTokenAsync(string nip, string token, CancellationToken ct);
    Task<string> SubmitInvoiceAsync(string sessionId, byte[] encryptedPayload, CancellationToken ct);
    Task<StatusDto> GetStatusAsync(string sessionId, string referenceId, CancellationToken ct);
    Task<byte[]> DownloadUpoAsync(string sessionId, string referenceId, CancellationToken ct);
}