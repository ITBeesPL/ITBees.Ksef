using System.Text.Json.Serialization;

namespace ITBees.Ksef.Models;

/// <summary>Which party of the invoice the querying context is — decides whether we get sales or purchase documents.</summary>
public static class InvoiceQuerySubjectType
{
    /// <summary>Invoices where the context is the seller (Podmiot1) — own sales.</summary>
    public const string Subject1 = "Subject1";

    /// <summary>Invoices where the context is the buyer (Podmiot2) — incoming cost invoices.</summary>
    public const string Subject2 = "Subject2";
}

/// <summary>Which date the range filter applies to.</summary>
public static class InvoiceQueryDateType
{
    /// <summary>Date the invoice was issued by the seller (data wystawienia).</summary>
    public const string Issue = "Issue";

    /// <summary>Date KSeF accepted the invoice (data przyjęcia) — the one that moves monotonically.</summary>
    public const string Invoicing = "Invoicing";

    /// <summary>Date the invoice became available to the buyer.</summary>
    public const string PermanentStorage = "PermanentStorage";
}

public class InvoiceQueryDateRange
{
    public string DateType { get; set; } = InvoiceQueryDateType.Invoicing;
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

/// <summary>Body of <c>POST /invoices/query/metadata</c>.</summary>
public class InvoiceMetadataQueryRequest
{
    public InvoiceQueryDateRange DateRange { get; set; } = new();

    /// <summary>See <see cref="InvoiceQuerySubjectType"/>.</summary>
    public string SubjectType { get; set; } = InvoiceQuerySubjectType.Subject2;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? KsefNumber { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InvoiceNumber { get; set; }
}

public class InvoiceMetadataQueryResponse
{
    /// <summary>True when more pages are waiting behind the current pageOffset/pageSize window.</summary>
    public bool HasMore { get; set; }

    public int? TotalCount { get; set; }

    public List<KsefInvoiceMetadata> Invoices { get; set; } = new();
}

public class KsefInvoiceMetadata
{
    public string KsefNumber { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>Issue date declared by the seller (P_1).</summary>
    public DateTimeOffset? IssueDate { get; set; }

    /// <summary>Date KSeF received the invoice.</summary>
    public DateTimeOffset? InvoicingDate { get; set; }

    public DateTimeOffset? AcquisitionDate { get; set; }
    public DateTimeOffset? PermanentStorageDate { get; set; }

    public KsefInvoiceParty? Seller { get; set; }
    public KsefInvoiceParty? Buyer { get; set; }

    public decimal? NetAmount { get; set; }
    public decimal? VatAmount { get; set; }
    public decimal? GrossAmount { get; set; }
    public string? Currency { get; set; }

    /// <summary>Document type reported by KSeF: VAT, KOR, ZAL, ROZ, UPR, KOR_ZAL, KOR_ROZ.</summary>
    public string? InvoiceType { get; set; }

    public string? FormCode { get; set; }
}

public class KsefInvoiceParty
{
    /// <summary>NIP for Polish entities; may be empty for consumers.</summary>
    public string? Nip { get; set; }

    /// <summary>Present on foreign counterparties instead of <see cref="Nip"/>.</summary>
    public string? Identifier { get; set; }

    public string? Name { get; set; }
}
