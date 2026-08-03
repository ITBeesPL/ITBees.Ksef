using ITBees.Ksef.Models;

namespace ITBees.Ksef;

/// <summary>
/// Reads documents that already live in KSeF: metadata search over the authenticated context
/// and download of the original FA XML. Complements <see cref="IKsefInvoiceService"/>, which writes.
/// </summary>
public interface IKsefInvoiceQueryService
{
    /// <summary>
    /// Returns every invoice matching the filter, following KSeF paging until it runs out of pages.
    /// </summary>
    Task<IReadOnlyList<KsefInvoiceMetadata>> QueryAsync(KsefInvoiceQueryFilter filter,
        CancellationToken ct = default);

    /// <summary>Downloads the original FA XML of a single invoice.</summary>
    Task<string> DownloadInvoiceXmlAsync(string ksefNumber, CancellationToken ct = default);
}

public class KsefInvoiceQueryFilter
{
    public DateTimeOffset From { get; set; }

    public DateTimeOffset To { get; set; }

    /// <summary>See <see cref="InvoiceQuerySubjectType"/>: Subject2 = cost invoices, Subject1 = own sales.</summary>
    public string SubjectType { get; set; } = InvoiceQuerySubjectType.Subject2;

    /// <summary>See <see cref="InvoiceQueryDateType"/>.</summary>
    public string DateType { get; set; } = InvoiceQueryDateType.Invoicing;

    /// <summary>Page size sent to KSeF; the service still returns all pages.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Safety valve so a wide date range cannot pull an unbounded number of documents.</summary>
    public int MaxInvoices { get; set; } = 2000;
}
