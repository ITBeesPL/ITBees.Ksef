namespace ITBees.Ksef.Core;

public interface IInvoiceRepository
{
    Task SaveDraftAsync(IInvoiceDocument doc, CancellationToken ct);
    Task MarkQueuedAsync(Guid id, CancellationToken ct);
    Task UpdateStateAsync(Guid id, InvoiceState state, string? ksefNumber = null, string? reason = null, CancellationToken ct = default);
    Task<IReadOnlyList<IInvoiceDocument>> GetPendingAsync(int take, CancellationToken ct);
}