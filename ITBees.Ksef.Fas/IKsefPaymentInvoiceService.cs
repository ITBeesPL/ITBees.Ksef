namespace ITBees.Ksef.Fas;

public interface IKsefPaymentInvoiceService
{
    /// <summary>
    /// Finds successfully paid payment sessions without an invoice and issues KSeF e-invoices for them.
    /// Returns the number of invoices sent in this pass.
    /// </summary>
    Task<int> IssuePendingInvoicesAsync(CancellationToken ct = default);
}
