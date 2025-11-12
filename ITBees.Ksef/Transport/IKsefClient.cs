using ITBees.Ksef.KsefV2;

namespace ITBees.Ksef.Transport;

public interface IKsefClient
{
    Task<string> OpenSessionAsync(string nip, string token, CancellationToken ct);
    Task<SubmitResult> SubmitInvoiceAsync(string sessionId, byte[] encryptedPayload, CancellationToken ct);
    Task<StatusResult> GetStatusAsync(string sessionId, string referenceId, CancellationToken ct);
    Task<byte[]> DownloadUpoAsync(string sessionId, string referenceId, CancellationToken ct);
}

