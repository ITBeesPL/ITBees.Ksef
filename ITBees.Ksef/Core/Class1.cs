namespace ITBees.Ksef.Core;

public record IssuerId(string Nip);
public enum InvoiceState { Draft, Validated, Queued, Sent, Accepted, Rejected, Failed }

public interface IInvoiceDocument
{
    Guid Id { get; }
    IssuerId Issuer { get; }
    string LocalNumber { get; }        // Twój numer
    string XmlRaw { get; }             // surowe FA(2)/FA(3)
    DateTime CreatedAt { get; }
}

public interface IInvoiceRepository
{
    Task SaveDraftAsync(IInvoiceDocument doc, CancellationToken ct);
    Task MarkQueuedAsync(Guid id, CancellationToken ct);
    Task UpdateStateAsync(Guid id, InvoiceState state, string? ksefNumber = null, string? reason = null, CancellationToken ct = default);
    Task<IReadOnlyList<IInvoiceDocument>> GetPendingAsync(int take, CancellationToken ct);
}

public interface IInvoicePrevalidator
{
    Task<ValidationResult> ValidateAsync(string xml, CancellationToken ct);
}

public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors);