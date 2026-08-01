using ITBees.Ksef.Invoicing;

namespace ITBees.Ksef;

/// <summary>
/// High-level facade: generates FA(3) XML and delivers it to KSeF within an online session,
/// returning the assigned KSeF number and (when available) the UPO confirmation.
/// </summary>
public interface IKsefInvoiceService
{
    /// <summary>Renders the invoice to FA(3) XML and sends it to KSeF.</summary>
    Task<KsefInvoiceSendResult> SendInvoiceAsync(KsefInvoice invoice, CancellationToken ct = default);

    /// <summary>Sends a ready FA(3) invoice XML to KSeF.</summary>
    Task<KsefInvoiceSendResult> SendInvoiceXmlAsync(string invoiceXml, CancellationToken ct = default);
}

public class KsefInvoiceSendResult
{
    public string SessionReferenceNumber { get; set; } = string.Empty;
    public string InvoiceReferenceNumber { get; set; } = string.Empty;

    /// <summary>Official KSeF number assigned to the invoice.</summary>
    public string KsefNumber { get; set; } = string.Empty;

    /// <summary>Date the invoice was received by KSeF (data przyjęcia — legal issue date in KSeF).</summary>
    public DateTimeOffset? AcquisitionDate { get; set; }

    /// <summary>UPO XML for this invoice, when it could be retrieved; otherwise null (may be fetched later).</summary>
    public string? UpoXml { get; set; }

    /// <summary>The FA(3) XML that was sent (useful for archiving).</summary>
    public string InvoiceXml { get; set; } = string.Empty;
}
