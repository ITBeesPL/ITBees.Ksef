namespace ITBees.Ksef.Fas;

public enum KsefInvoiceRecordStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,

    /// <summary>Payment did not require an invoice (e.g. zero-value trial plan).</summary>
    Skipped = 3
}

/// <summary>
/// Outbox record of a KSeF e-invoice issued for a successfully paid <c>PaymentSession</c>.
/// Also acts as the invoice number sequencer: (Year, Month, SequenceNumber) is unique.
/// </summary>
public class KsefInvoiceRecord
{
    public int Id { get; set; }

    public Guid PaymentSessionGuid { get; set; }

    /// <summary>Local invoice number placed in FA(3) field P_2, e.g. "FV/12/08/2026".</summary>
    public string InvoiceNumber { get; set; } = string.Empty;

    public int Year { get; set; }
    public int Month { get; set; }
    public int SequenceNumber { get; set; }

    public KsefInvoiceRecordStatus Status { get; set; }

    /// <summary>Official number assigned by KSeF after acceptance.</summary>
    public string? KsefNumber { get; set; }

    public string? KsefSessionReferenceNumber { get; set; }

    /// <summary>FA(3) XML that was sent (archival copy).</summary>
    public string? InvoiceXml { get; set; }

    /// <summary>UPO confirmation XML, when it was available at send time.</summary>
    public string? UpoXml { get; set; }

    public int Attempts { get; set; }
    public string? LastError { get; set; }

    public DateTime Created { get; set; }
    public DateTime? SentDate { get; set; }
}
