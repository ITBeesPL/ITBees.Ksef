namespace ITBees.Ksef.Invoicing;

/// <summary>
/// Simplified domain model of a VAT sales invoice, rendered to FA(3) XML by <see cref="Fa3XmlGenerator"/>.
/// Designed for typical SaaS/subscription sales (single or few lines, one currency).
/// </summary>
public class KsefInvoice
{
    /// <summary>Invoice number (P_2), e.g. "FV/2026/08/00123". Must be unique per seller.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Issue date (P_1).</summary>
    public DateOnly IssueDate { get; set; }

    /// <summary>Date of sale / service completion (P_6). Defaults to <see cref="IssueDate"/> when null.</summary>
    public DateOnly? SaleDate { get; set; }

    /// <summary>ISO 4217 currency code (KodWaluty), e.g. "PLN".</summary>
    public string Currency { get; set; } = "PLN";

    /// <summary>Seller (Podmiot1). When null, the generator uses <c>KsefOptions.Seller</c>.</summary>
    public KsefParty? Seller { get; set; }

    /// <summary>Buyer (Podmiot2).</summary>
    public KsefParty Buyer { get; set; } = new();

    public List<KsefInvoiceLine> Lines { get; set; } = new();

    /// <summary>True when the invoice was already paid (e.g. Stripe checkout) — emits Platnosc/Zaplacono.</summary>
    public bool IsPaid { get; set; }

    /// <summary>Payment date, required when <see cref="IsPaid"/> is true.</summary>
    public DateOnly? PaymentDate { get; set; }
}

public class KsefParty
{
    /// <summary>Polish NIP (10 digits). Leave null for consumers (B2C) — the generator emits BrakID.</summary>
    public string? Nip { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Street with numbers, e.g. "ul. Polna 1".</summary>
    public string AddressLine1 { get; set; } = string.Empty;

    /// <summary>Postal code and city, e.g. "00-001 Warszawa".</summary>
    public string AddressLine2 { get; set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2 country code (KodKraju).</summary>
    public string CountryCode { get; set; } = "PL";

    public string? Email { get; set; }
    public string? Phone { get; set; }
}

public class KsefInvoiceLine
{
    /// <summary>Name of the goods/service (P_7).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Unit of measure (P_8A), e.g. "szt.", "usł.".</summary>
    public string Unit { get; set; } = "szt.";

    /// <summary>Quantity (P_8B).</summary>
    public decimal Quantity { get; set; } = 1m;

    /// <summary>Net unit price (P_9A).</summary>
    public decimal UnitNetPrice { get; set; }

    /// <summary>Net line value (P_11). When null, computed as Quantity × UnitNetPrice rounded to 2 decimals.</summary>
    public decimal? NetValue { get; set; }

    /// <summary>VAT rate in percent: 23, 8, 5 or 0.</summary>
    public int VatRate { get; set; } = 23;

    public decimal GetNetValue() => NetValue ?? Math.Round(Quantity * UnitNetPrice, 2, MidpointRounding.AwayFromZero);
}
