namespace ITBees.Ksef.Core;

public interface IInvoicePrevalidator
{
    Task<ValidationResult> ValidateAsync(string xml, CancellationToken ct);
}