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

    /// <summary>
    /// Correction data. When set, the document is rendered as a correcting invoice
    /// (RodzajFaktury = KOR) instead of an ordinary one; null renders a plain VAT invoice.
    /// </summary>
    public KsefInvoiceCorrection? Correction { get; set; }

    /// <summary>
    /// Advance-invoice data (art. 106f of the Polish VAT act). When set, the document is rendered
    /// as an advance invoice (RodzajFaktury = ZAL): the aggregates and P_15 come from the received
    /// payment split per VAT rate (KP = ZB × SP / (100 + SP)), <see cref="Lines"/> must stay empty,
    /// and the order/contract goes into the Zamowienie node. <see cref="SaleDate"/> then carries
    /// the date the payment was received (P_6). Mutually exclusive with <see cref="Correction"/>.
    /// </summary>
    public KsefAdvance? Advance { get; set; }

    /// <summary>True when the invoice was already paid (e.g. Stripe checkout) — emits Platnosc/Zaplacono.</summary>
    public bool IsPaid { get; set; }

    /// <summary>Payment date, required when <see cref="IsPaid"/> is true.</summary>
    public DateOnly? PaymentDate { get; set; }

    /// <summary>
    /// Payment due date — emits <c>Platnosc/TerminPlatnosci/Termin</c>. Independent of
    /// <see cref="IsPaid"/>: the schema allows both on one document, though senders typically
    /// omit the due date once the invoice is paid.
    /// </summary>
    public DateOnly? PaymentDueDate { get; set; }

    /// <summary>Seller's bank account for the payment — emits <c>Platnosc/RachunekBankowy</c>.</summary>
    public KsefBankAccount? BankAccount { get; set; }

    /// <summary>
    /// Free-text remarks of the seller, emitted as <c>Stopka/Informacje/StopkaFaktury</c> —
    /// the schema's free-form text field (max 3500 characters, newlines allowed).
    /// Null or whitespace emits no Stopka element at all.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Legal basis of the VAT exemption (Adnotacje/Zwolnienie/P_19A), e.g. "art. 43 ust. 1 pkt 19
    /// ustawy o VAT". Required when any line (or advance payment) uses
    /// <see cref="KsefVatRateKind.Exempt"/>; ignored otherwise. Max 240 characters (TZnakowy).
    /// </summary>
    public string? ExemptionBasis { get; set; }
}

/// <summary>Advance-invoice data (RodzajFaktury = ZAL): the received payment and the order it prepays.</summary>
public class KsefAdvance
{
    /// <summary>
    /// Received gross payment allocated per VAT rate. Drives the P_13_x/P_14_x aggregates and P_15;
    /// per rate the tax is KP = ZB × SP / (100 + SP) as required by art. 106f ust. 1 pkt 3.
    /// </summary>
    public List<KsefAdvancePayment> Payments { get; set; } = new();

    /// <summary>Total order/contract value including VAT (Zamowienie/WartoscZamowienia).</summary>
    public decimal OrderGrossTotal { get; set; }

    /// <summary>
    /// Order/contract rows (Zamowienie/ZamowienieWiersz) — the goods/services, quantities, net
    /// values and VAT rates of the whole order, required by art. 106f ust. 1 pkt 4.
    /// </summary>
    public List<KsefInvoiceLine> OrderLines { get; set; } = new();
}

/// <summary>Part of the received advance payment attributable to one VAT rate.</summary>
public class KsefAdvancePayment
{
    public int VatRate { get; set; } = 23;

    /// <summary>Meaning of <see cref="VatRate"/> — see <see cref="KsefInvoiceLine.VatRateKind"/>.</summary>
    public KsefVatRateKind VatRateKind { get; set; } = KsefVatRateKind.Standard;

    /// <summary>Gross amount received for this rate (ZB in the KP = ZB × SP / (100 + SP) formula).</summary>
    public decimal GrossAmount { get; set; }
}

/// <summary>When the correction takes effect in the VAT ledger (TypKorekty).</summary>
public enum KsefCorrectionType
{
    /// <summary>In the period of the original invoice — typically a mistake on the original.</summary>
    OriginalPeriod = 1,

    /// <summary>In the period the correction is issued — returns, discounts granted after the sale.</summary>
    CorrectionPeriod = 2,

    /// <summary>In another period, including when individual lines take effect on different dates.</summary>
    OtherPeriod = 3
}

/// <summary>
/// Identifies the invoice being corrected and why (FA(3): PrzyczynaKorekty, TypKorekty, DaneFaKorygowanej).
/// </summary>
public class KsefInvoiceCorrection
{
    /// <summary>Reason for the correction (PrzyczynaKorekty) — required on a correcting invoice by art. 106j.</summary>
    public string Reason { get; set; } = string.Empty;

    public KsefCorrectionType Type { get; set; } = KsefCorrectionType.OriginalPeriod;

    /// <summary>Number of the corrected invoice (NrFaKorygowanej).</summary>
    public string CorrectedNumber { get; set; } = string.Empty;

    /// <summary>Issue date of the corrected invoice (DataWystFaKorygowanej).</summary>
    public DateOnly CorrectedIssueDate { get; set; }

    /// <summary>
    /// KSeF number of the corrected invoice. Null means it was issued outside KSeF, which the
    /// generator reports as NrKSeFN instead of NrKSeFFaKorygowanej.
    /// </summary>
    public string? CorrectedKsefNumber { get; set; }
}

/// <summary>Bank account the buyer should pay to (FA(3): Platnosc/RachunekBankowy).</summary>
public class KsefBankAccount
{
    /// <summary>
    /// Full account number (NrRB) — 10 to 34 characters after the generator strips whitespace
    /// and dashes, typically a 26-digit Polish NRB or an IBAN.
    /// </summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Optional account label shown to the buyer (OpisRachunku), e.g. "Rachunek firmowy".</summary>
    public string? Description { get; set; }
}

/// <summary>
/// A party of the invoice. The buyer is identified by exactly one of: <see cref="Nip"/> (Polish
/// taxpayer), <see cref="EuVatNumber"/> (EU business, KodUE + NrVatUE), <see cref="ForeignTaxId"/>
/// (any other tax identifier, KodKraju + NrID) — checked in that order — or none of them
/// (consumer, BrakID). The seller must always have a NIP.
/// </summary>
public class KsefParty
{
    /// <summary>Polish NIP (10 digits). Leave null for consumers (B2C) — the generator emits BrakID.</summary>
    public string? Nip { get; set; }

    /// <summary>
    /// EU VAT number without the country prefix (NrVatUE, up to 12 characters: digits, capital
    /// letters, '+' and '*'). Used when <see cref="Nip"/> is empty; the prefix goes to
    /// <see cref="EuVatCountryCode"/>.
    /// </summary>
    public string? EuVatNumber { get; set; }

    /// <summary>
    /// VAT prefix of the EU member state (KodUE), e.g. "DE"; Greece is "EL" and Northern Ireland
    /// "XI" as in the VIES register. Defaults to <see cref="CountryCode"/> when empty.
    /// </summary>
    public string? EuVatCountryCode { get; set; }

    /// <summary>
    /// Tax identifier issued outside the EU (NrID, up to 50 characters), used when neither
    /// <see cref="Nip"/> nor <see cref="EuVatNumber"/> is set. The issuing country goes to
    /// <see cref="ForeignTaxIdCountryCode"/>.
    /// </summary>
    public string? ForeignTaxId { get; set; }

    /// <summary>ISO 3166-1 alpha-2 code of the country that issued <see cref="ForeignTaxId"/> (KodKraju). Defaults to <see cref="CountryCode"/> when empty.</summary>
    public string? ForeignTaxIdCountryCode { get; set; }

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

    /// <summary>
    /// VAT rate in percent (23, 22, 8, 7, 5, 4 or 0) when <see cref="VatRateKind"/> is
    /// <see cref="KsefVatRateKind.Standard"/>; must be 0 for every other kind.
    /// </summary>
    public int VatRate { get; set; } = 23;

    /// <summary>
    /// What the rate means — a plain percentage, or one of the untaxed cases FA(3) keeps apart
    /// (WDT, export, exempt, reverse charge, not subject to Polish VAT). Drives P_12 and the
    /// aggregate field the line is summed into.
    /// </summary>
    public KsefVatRateKind VatRateKind { get; set; } = KsefVatRateKind.Standard;

    /// <summary>
    /// Marks the row as the state <em>before</em> the correction (StanPrzed). On a correcting invoice
    /// the before/after states are listed as separate rows; the aggregates then carry the difference,
    /// so rows flagged here are subtracted rather than added. Only valid when
    /// <see cref="KsefInvoice.Correction"/> is set.
    /// </summary>
    public bool StateBeforeCorrection { get; set; }

    public decimal GetNetValue() => NetValue ?? Math.Round(Quantity * UnitNetPrice, 2, MidpointRounding.AwayFromZero);
}
