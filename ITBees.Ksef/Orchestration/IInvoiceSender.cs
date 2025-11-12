using ITBees.Ksef.Core;

namespace ITBees.Ksef.Orchestration;

public interface IInvoiceSender
{
    Task QueueAsync(IInvoiceDocument doc, CancellationToken ct);
    Task ProcessPendingAsync(int batchSize, CancellationToken ct); // wywołasz z własnego crona
}