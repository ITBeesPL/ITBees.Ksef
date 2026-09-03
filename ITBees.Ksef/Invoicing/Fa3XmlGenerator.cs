using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ITBees.Ksef.Configuration;
using Microsoft.Extensions.Options;

namespace ITBees.Ksef.Invoicing;

/// <summary>
/// Renders <see cref="KsefInvoice"/> to FA(3) XML
/// (schema http://crd.gov.pl/wzor/2025/06/25/13775/, kodSystemowy "FA (3)", wersjaSchemy "1-0E").
/// </summary>
public class Fa3XmlGenerator : IFa3XmlGenerator
{
    public static readonly XNamespace Fa3Namespace = "http://crd.gov.pl/wzor/2025/06/25/13775/";

    private static readonly Regex EuVatNumberPattern = new(@"^(\d|[A-Z]|\+|\*){1,12}$", RegexOptions.Compiled);

    private readonly KsefOptions _options;

    public Fa3XmlGenerator(IOptions<KsefOptions> options)
    {
        _options = options.Value;
    }

    public string Generate(KsefInvoice invoice) => Generate(invoice, DateTimeOffset.UtcNow);

    public string Generate(KsefInvoice invoice, DateTimeOffset generatedAtUtc)
    {
        Validate(invoice);

        var ns = Fa3Namespace;
        var seller = invoice.Seller ?? SellerFromOptions();
        var saleDate = invoice.SaleDate ?? invoice.IssueDate;

        // On a correction the aggregates carry the difference rather than the document value:
        // "before" rows are subtracted, "after" rows added. A plain invoice has nothing to subtract.
        // On an advance invoice they come from the received payment instead of the rows: per rate
        // the tax is KP = ZB × SP / (100 + SP) computed from the gross amount (art. 106f) — deriving
        // it from the net again could drift by a grosz, so net is what remains after the tax.
        // The key is the (rate, kind) pair: two 0% lines can land in different aggregate fields
        // (domestic 0%, WDT, export, exempt…), so the percentage alone would merge them.
        Dictionary<(int Rate, KsefVatRateKind Kind), (decimal Net, decimal Vat)> vatTotals;
        if (invoice.Advance != null)
        {
            vatTotals = invoice.Advance.Payments
                .GroupBy(p => (p.VatRate, p.VatRateKind))
                .ToDictionary(g => g.Key, g =>
                {
                    var gross = g.Sum(p => p.GrossAmount);
                    var vat = KsefVatRates.IsTaxed(g.Key.VatRateKind)
                        ? Math.Round(gross * g.Key.VatRate / (100m + g.Key.VatRate), 2, MidpointRounding.AwayFromZero)
                        : 0m;
                    return (gross - vat, vat);
                });
        }
        else
        {
            vatTotals = invoice.Lines
                .GroupBy(l => (l.VatRate, l.VatRateKind))
                .ToDictionary(g => g.Key, g =>
                {
                    var net = g.Sum(l => l.StateBeforeCorrection ? -l.GetNetValue() : l.GetNetValue());
                    var vat = KsefVatRates.IsTaxed(g.Key.VatRateKind)
                        ? Math.Round(net * g.Key.VatRate / 100m, 2, MidpointRounding.AwayFromZero)
                        : 0m;
                    return (net, vat);
                });
        }

        var totalNet = vatTotals.Values.Sum(x => x.Net);
        var totalVat = vatTotals.Values.Sum(x => x.Vat);
        var totalGross = totalNet + totalVat;

        var fa = new XElement(ns + "Fa",
            new XElement(ns + "KodWaluty", invoice.Currency),
            new XElement(ns + "P_1", FormatDate(invoice.IssueDate)),
            new XElement(ns + "P_2", invoice.Number),
            new XElement(ns + "P_6", FormatDate(saleDate)));

        AppendVatAggregates(fa, ns, vatTotals);
        fa.Add(new XElement(ns + "P_15", FormatAmount(totalGross)));
        fa.Add(BuildAdnotacje(ns, invoice));
        fa.Add(new XElement(ns + "RodzajFaktury",
            invoice.Correction != null ? "KOR" : invoice.Advance != null ? "ZAL" : "VAT"));
        if (invoice.Correction != null)
            AppendCorrectionData(fa, ns, invoice.Correction);

        var lineNumber = 0;
        foreach (var line in invoice.Lines)
        {
            lineNumber++;
            var row = new XElement(ns + "FaWiersz",
                new XElement(ns + "NrWierszaFa", lineNumber),
                new XElement(ns + "P_7", line.Name),
                new XElement(ns + "P_8A", line.Unit),
                new XElement(ns + "P_8B", FormatQuantity(line.Quantity)),
                new XElement(ns + "P_9A", FormatAmount(line.UnitNetPrice)),
                new XElement(ns + "P_11", FormatAmount(line.GetNetValue())),
                new XElement(ns + "P_12", KsefVatRates.ToP12(line.VatRate, line.VatRateKind)));

            // StanPrzed closes the row sequence in the schema, so it has to be added last.
            if (line.StateBeforeCorrection)
                row.Add(new XElement(ns + "StanPrzed", 1));

            fa.Add(row);
        }

        var payment = BuildPlatnosc(ns, invoice);
        if (payment != null)
            fa.Add(payment);

        // Zamowienie closes the Fa sequence in the schema, so it goes after the payment block.
        if (invoice.Advance != null)
            fa.Add(BuildZamowienie(ns, invoice.Advance));

        var faktura = new XElement(ns + "Faktura",
            new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
            new XElement(ns + "Naglowek",
                new XElement(ns + "KodFormularza",
                    new XAttribute("kodSystemowy", "FA (3)"),
                    new XAttribute("wersjaSchemy", "1-0E"),
                    "FA"),
                new XElement(ns + "WariantFormularza", 3),
                new XElement(ns + "DataWytworzeniaFa",
                    generatedAtUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
                new XElement(ns + "SystemInfo", _options.SystemInfo)),
            BuildParty(ns, "Podmiot1", seller),
            BuildParty(ns, "Podmiot2", invoice.Buyer),
            fa);

        // StopkaFaktury is TTekstowy (min 1 char), so blank notes must emit no Stopka at all.
        if (!string.IsNullOrWhiteSpace(invoice.Notes))
            faktura.Add(new XElement(ns + "Stopka",
                new XElement(ns + "Informacje",
                    new XElement(ns + "StopkaFaktury", invoice.Notes))));

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), faktura);

        var builder = new StringBuilder();
        using (var writer = new Utf8StringWriter(builder))
        {
            document.Save(writer);
        }

        return builder.ToString();
    }

    private static void Validate(KsefInvoice invoice)
    {
        if (string.IsNullOrWhiteSpace(invoice.Number))
            throw new ArgumentException("Invoice number (P_2) is required.", nameof(invoice));
        if (invoice.IssueDate == default)
            throw new ArgumentException("Invoice issue date (P_1) is required.", nameof(invoice));
        if (invoice.Lines.Count == 0 && invoice.Advance == null)
            throw new ArgumentException("Invoice must contain at least one line.", nameof(invoice));
        if (string.IsNullOrWhiteSpace(invoice.Buyer.Name))
            throw new ArgumentException("Buyer name is required.", nameof(invoice));
        if (invoice.Notes is { Length: > 3500 })
            throw new ArgumentException(
                "Notes exceed 3500 characters — the limit of StopkaFaktury (TTekstowy) in FA(3).", nameof(invoice));

        ValidateBuyerIdentification(invoice.Buyer);

        // TNrRB constrains the account number to 10–34 characters; whitespace and dashes are
        // stripped before the check because the generator strips them on output too.
        if (invoice.BankAccount != null
            && NormalizeAccountNumber(invoice.BankAccount.Number).Length is < 10 or > 34)
            throw new ArgumentException(
                "Bank account number (NrRB) must be 10–34 characters long.", nameof(invoice));

        var rates = invoice.Lines.Select(l => (l.VatRate, l.VatRateKind))
            .Concat(invoice.Advance?.Payments.Select(p => (p.VatRate, p.VatRateKind))
                    ?? Enumerable.Empty<(int, KsefVatRateKind)>())
            .Concat(invoice.Advance?.OrderLines.Select(l => (l.VatRate, l.VatRateKind))
                    ?? Enumerable.Empty<(int, KsefVatRateKind)>())
            .ToList();

        var unsupportedRate = rates
            .Where(r => r.Item2 == KsefVatRateKind.Standard && !KsefVatRates.SupportedStandardRates.Contains(r.Item1))
            .Select(r => (int?)r.Item1).FirstOrDefault();
        if (unsupportedRate != null)
            throw new NotSupportedException(
                $"VAT rate {unsupportedRate}% is not supported by this generator (supported: 23, 22, 8, 7, 5, 4, 0).");

        // A non-standard kind has no percentage by definition; a leftover 23 next to "zw" would
        // mean the caller mapped the line wrongly, and the totals would silently disagree.
        var mismatched = rates.FirstOrDefault(r => r.Item2 != KsefVatRateKind.Standard && r.Item1 != 0);
        if (mismatched.Item2 != KsefVatRateKind.Standard)
            throw new ArgumentException(
                $"VAT rate kind {mismatched.Item2} requires VatRate = 0, got {mismatched.Item1}.", nameof(invoice));

        if (rates.Any(r => r.Item2 == KsefVatRateKind.Exempt))
        {
            if (string.IsNullOrWhiteSpace(invoice.ExemptionBasis))
                throw new ArgumentException(
                    "An exempt (zw) line requires the legal basis of the exemption (ExemptionBasis → P_19A).",
                    nameof(invoice));
            if (invoice.ExemptionBasis.Length > 240)
                throw new ArgumentException(
                    "ExemptionBasis exceeds 240 characters — the limit of P_19A (TZnakowy) in FA(3).", nameof(invoice));
        }

        ValidateAdvance(invoice);

        if (invoice.Correction == null)
        {
            if (invoice.Lines.Any(l => l.StateBeforeCorrection))
                throw new ArgumentException(
                    "Lines marked as the state before correction require Correction to be set.", nameof(invoice));
            return;
        }

        if (string.IsNullOrWhiteSpace(invoice.Correction.Reason))
            throw new ArgumentException("Correction reason (PrzyczynaKorekty) is required.", nameof(invoice));
        if (string.IsNullOrWhiteSpace(invoice.Correction.CorrectedNumber))
            throw new ArgumentException("Corrected invoice number (NrFaKorygowanej) is required.", nameof(invoice));
        if (invoice.Correction.CorrectedIssueDate == default)
            throw new ArgumentException(
                "Corrected invoice issue date (DataWystFaKorygowanej) is required.", nameof(invoice));
    }

    private static void ValidateBuyerIdentification(KsefParty buyer)
    {
        if (!string.IsNullOrWhiteSpace(buyer.Nip))
            return;

        if (!string.IsNullOrWhiteSpace(buyer.EuVatNumber))
        {
            if (!EuVatNumberPattern.IsMatch(buyer.EuVatNumber.Trim()))
                throw new ArgumentException(
                    "EU VAT number (NrVatUE) must be 1–12 characters: digits, capital letters, '+' or '*' — without the country prefix.",
                    nameof(buyer));
            var prefix = KsefEuVatCountries.ToVatPrefix(buyer.EuVatCountryCode ?? buyer.CountryCode);
            if (!KsefEuVatCountries.Contains(prefix))
                throw new ArgumentException(
                    $"'{prefix}' is not an EU VAT prefix (KodUE) — use ForeignTaxId for a buyer outside the EU.",
                    nameof(buyer));
            return;
        }

        if (!string.IsNullOrWhiteSpace(buyer.ForeignTaxId) && buyer.ForeignTaxId.Trim().Length > 50)
            throw new ArgumentException(
                "Foreign tax identifier (NrID) must not exceed 50 characters.", nameof(buyer));
    }

    private static void ValidateAdvance(KsefInvoice invoice)
    {
        if (invoice.Advance == null)
            return;

        // A correction of an advance invoice is a distinct document kind (KOR_ZAL) with its own
        // rules for the Zamowienie node — refuse rather than emit something the schema rejects.
        if (invoice.Correction != null)
            throw new ArgumentException(
                "An advance invoice cannot be a correction (KOR_ZAL is not supported yet).", nameof(invoice));
        if (invoice.Lines.Count > 0)
            throw new ArgumentException(
                "An advance invoice has no FaWiersz rows — put the order into Advance.OrderLines.",
                nameof(invoice));
        if (invoice.Advance.Payments.Count == 0)
            throw new ArgumentException(
                "An advance invoice requires at least one received payment (Advance.Payments).", nameof(invoice));
        if (invoice.Advance.Payments.Any(p => p.GrossAmount <= 0))
            throw new ArgumentException("Advance payment amounts must be positive.", nameof(invoice));
        if (invoice.Advance.OrderLines.Count == 0)
            throw new ArgumentException(
                "An advance invoice requires the order data (Advance.OrderLines) — art. 106f ust. 1 pkt 4.",
                nameof(invoice));
        if (invoice.Advance.OrderGrossTotal <= 0)
            throw new ArgumentException(
                "Order gross total (WartoscZamowienia) must be positive.", nameof(invoice));
    }

    /// <summary>
    /// Payment terms node. Schema order within Platnosc: Zaplacono/DataZaplaty, then
    /// TerminPlatnosci, then RachunekBankowy. Returns null when there is nothing to say —
    /// an empty Platnosc element is pointless, though the schema would accept it.
    /// </summary>
    private static XElement? BuildPlatnosc(XNamespace ns, KsefInvoice invoice)
    {
        var element = new XElement(ns + "Platnosc");

        if (invoice.IsPaid)
        {
            element.Add(new XElement(ns + "Zaplacono", "1"));
            element.Add(new XElement(ns + "DataZaplaty", FormatDate(invoice.PaymentDate ?? invoice.IssueDate)));
        }

        if (invoice.PaymentDueDate != null)
            element.Add(new XElement(ns + "TerminPlatnosci",
                new XElement(ns + "Termin", FormatDate(invoice.PaymentDueDate.Value))));

        if (invoice.BankAccount != null)
        {
            var account = new XElement(ns + "RachunekBankowy",
                new XElement(ns + "NrRB", NormalizeAccountNumber(invoice.BankAccount.Number)));
            if (!string.IsNullOrWhiteSpace(invoice.BankAccount.Description))
                account.Add(new XElement(ns + "OpisRachunku", invoice.BankAccount.Description));
            element.Add(account);
        }

        return element.HasElements ? element : null;
    }

    private static string NormalizeAccountNumber(string number) =>
        new(number.Where(x => !char.IsWhiteSpace(x) && x != '-').ToArray());

    /// <summary>Order/contract node required on an advance invoice (art. 106f ust. 1 pkt 4).</summary>
    private static XElement BuildZamowienie(XNamespace ns, KsefAdvance advance)
    {
        var element = new XElement(ns + "Zamowienie",
            new XElement(ns + "WartoscZamowienia", FormatAmount(advance.OrderGrossTotal)));

        var rowNumber = 0;
        foreach (var line in advance.OrderLines)
        {
            rowNumber++;
            var net = line.GetNetValue();
            var vat = KsefVatRates.IsTaxed(line.VatRateKind)
                ? Math.Round(net * line.VatRate / 100m, 2, MidpointRounding.AwayFromZero)
                : 0m;
            element.Add(new XElement(ns + "ZamowienieWiersz",
                new XElement(ns + "NrWierszaZam", rowNumber),
                new XElement(ns + "P_7Z", line.Name),
                new XElement(ns + "P_8AZ", line.Unit),
                new XElement(ns + "P_8BZ", FormatQuantity(line.Quantity)),
                new XElement(ns + "P_9AZ", FormatAmount(line.UnitNetPrice)),
                new XElement(ns + "P_11NettoZ", FormatAmount(net)),
                new XElement(ns + "P_11VatZ", FormatAmount(vat)),
                new XElement(ns + "P_12Z", KsefVatRates.ToP12(line.VatRate, line.VatRateKind))));
        }

        return element;
    }

    /// <summary>Emits PrzyczynaKorekty / TypKorekty / DaneFaKorygowanej, which follow RodzajFaktury in the schema.</summary>
    private static void AppendCorrectionData(XElement fa, XNamespace ns, KsefInvoiceCorrection correction)
    {
        fa.Add(new XElement(ns + "PrzyczynaKorekty", correction.Reason));
        fa.Add(new XElement(ns + "TypKorekty", (int)correction.Type));

        var corrected = new XElement(ns + "DaneFaKorygowanej",
            new XElement(ns + "DataWystFaKorygowanej", FormatDate(correction.CorrectedIssueDate)),
            new XElement(ns + "NrFaKorygowanej", correction.CorrectedNumber));

        if (string.IsNullOrWhiteSpace(correction.CorrectedKsefNumber))
        {
            // The corrected invoice never went through KSeF (paper / pre-KSeF document).
            corrected.Add(new XElement(ns + "NrKSeFN", 1));
        }
        else
        {
            corrected.Add(new XElement(ns + "NrKSeF", 1));
            corrected.Add(new XElement(ns + "NrKSeFFaKorygowanej", correction.CorrectedKsefNumber));
        }

        fa.Add(corrected);
    }

    private KsefParty SellerFromOptions()
    {
        var seller = _options.Seller ?? throw new InvalidOperationException(
            "Invoice has no Seller and KsefOptions.Seller is not configured.");
        return new KsefParty
        {
            Nip = seller.Nip,
            Name = seller.Name,
            AddressLine1 = seller.AddressLine1,
            AddressLine2 = seller.AddressLine2,
            CountryCode = seller.CountryCode,
            Email = seller.Email,
            Phone = seller.Phone
        };
    }

    /// <summary>
    /// Maps per-(rate, kind) net and tax totals to the FA(3) aggregate fields, in schema order:
    /// 23/22% → P_13_1/P_14_1, 8/7% → P_13_2/P_14_2, 5/4% → P_13_3/P_14_3, domestic 0% → P_13_6_1,
    /// WDT → P_13_6_2, export → P_13_6_3, exempt → P_13_7, np I → P_13_8, np II → P_13_9,
    /// reverse charge → P_13_10. The untaxed fields have no P_14 counterpart.
    /// </summary>
    private static void AppendVatAggregates(XElement fa, XNamespace ns,
        Dictionary<(int Rate, KsefVatRateKind Kind), (decimal Net, decimal Vat)> vatTotals)
    {
        void Append(Func<(int Rate, KsefVatRateKind Kind), bool> selector, string netField, string? vatField)
        {
            var matching = vatTotals.Where(x => selector(x.Key)).Select(x => x.Value).ToList();
            if (matching.Count == 0)
                return;

            fa.Add(new XElement(ns + netField, FormatAmount(matching.Sum(x => x.Net))));
            if (vatField != null)
                fa.Add(new XElement(ns + vatField, FormatAmount(matching.Sum(x => x.Vat))));
        }

        bool Standard((int Rate, KsefVatRateKind Kind) key, params int[] rates) =>
            key.Kind == KsefVatRateKind.Standard && rates.Contains(key.Rate);

        Append(k => Standard(k, 23, 22), "P_13_1", "P_14_1");
        Append(k => Standard(k, 8, 7), "P_13_2", "P_14_2");
        Append(k => Standard(k, 5, 4), "P_13_3", "P_14_3");
        Append(k => Standard(k, 0), "P_13_6_1", null);
        Append(k => k.Kind == KsefVatRateKind.ZeroIntraCommunity, "P_13_6_2", null);
        Append(k => k.Kind == KsefVatRateKind.ZeroExport, "P_13_6_3", null);
        Append(k => k.Kind == KsefVatRateKind.Exempt, "P_13_7", null);
        Append(k => k.Kind == KsefVatRateKind.NotSubjectNonEu, "P_13_8", null);
        Append(k => k.Kind == KsefVatRateKind.NotSubjectEu, "P_13_9", null);
        Append(k => k.Kind == KsefVatRateKind.ReverseCharge, "P_13_10", null);
    }

    /// <summary>
    /// Adnotacje: mostly "not applicable" markers (2 = no), except P_18 (reverse charge) and the
    /// Zwolnienie block, which follow from the rate kinds on the document. P_19 + P_19A replace
    /// P_19N when any line is exempt; the schema makes the two mutually exclusive.
    /// </summary>
    private static XElement BuildAdnotacje(XNamespace ns, KsefInvoice invoice)
    {
        var kinds = invoice.Lines.Select(l => l.VatRateKind)
            .Concat(invoice.Advance?.Payments.Select(p => p.VatRateKind) ?? Enumerable.Empty<KsefVatRateKind>())
            .Concat(invoice.Advance?.OrderLines.Select(l => l.VatRateKind) ?? Enumerable.Empty<KsefVatRateKind>())
            .ToHashSet();

        var exemption = kinds.Contains(KsefVatRateKind.Exempt)
            ? new XElement(ns + "Zwolnienie",
                new XElement(ns + "P_19", 1),
                new XElement(ns + "P_19A", invoice.ExemptionBasis!.Trim()))
            : new XElement(ns + "Zwolnienie", new XElement(ns + "P_19N", 1));

        return new XElement(ns + "Adnotacje",
            new XElement(ns + "P_16", 2),
            new XElement(ns + "P_17", 2),
            new XElement(ns + "P_18", kinds.Contains(KsefVatRateKind.ReverseCharge) ? 1 : 2),
            new XElement(ns + "P_18A", 2),
            exemption,
            new XElement(ns + "NoweSrodkiTransportu", new XElement(ns + "P_22N", 1)),
            new XElement(ns + "P_23", 2),
            new XElement(ns + "PMarzy", new XElement(ns + "P_PMarzyN", 1)));
    }

    private static XElement BuildParty(XNamespace ns, string elementName, KsefParty party)
    {
        var identification = new XElement(ns + "DaneIdentyfikacyjne");
        if (!string.IsNullOrWhiteSpace(party.Nip))
        {
            identification.Add(new XElement(ns + "NIP", party.Nip));
        }
        else if (elementName != "Podmiot2")
        {
            throw new ArgumentException("Seller (Podmiot1) must have a NIP.");
        }
        else if (!string.IsNullOrWhiteSpace(party.EuVatNumber))
        {
            identification.Add(new XElement(ns + "KodUE",
                KsefEuVatCountries.ToVatPrefix(party.EuVatCountryCode ?? party.CountryCode)));
            identification.Add(new XElement(ns + "NrVatUE", party.EuVatNumber.Trim()));
        }
        else if (!string.IsNullOrWhiteSpace(party.ForeignTaxId))
        {
            var country = (party.ForeignTaxIdCountryCode ?? party.CountryCode).Trim().ToUpperInvariant();
            if (country.Length > 0)
                identification.Add(new XElement(ns + "KodKraju", country));
            identification.Add(new XElement(ns + "NrID", party.ForeignTaxId.Trim()));
        }
        else
        {
            identification.Add(new XElement(ns + "BrakID", 1));
        }

        identification.Add(new XElement(ns + "Nazwa", party.Name));

        var element = new XElement(ns + elementName,
            identification,
            new XElement(ns + "Adres",
                new XElement(ns + "KodKraju", party.CountryCode),
                new XElement(ns + "AdresL1", party.AddressLine1),
                new XElement(ns + "AdresL2", party.AddressLine2)));

        if (!string.IsNullOrWhiteSpace(party.Email) || !string.IsNullOrWhiteSpace(party.Phone))
        {
            var contact = new XElement(ns + "DaneKontaktowe");
            if (!string.IsNullOrWhiteSpace(party.Email))
                contact.Add(new XElement(ns + "Email", party.Email));
            if (!string.IsNullOrWhiteSpace(party.Phone))
                contact.Add(new XElement(ns + "Telefon", party.Phone));
            element.Add(contact);
        }

        if (elementName == "Podmiot2")
        {
            // Mandatory FA(3) markers: 2 = the invoice concerns neither a subordinate
            // local-government unit (JST) nor a VAT group member (GV).
            element.Add(new XElement(ns + "JST", 2));
            element.Add(new XElement(ns + "GV", 2));
        }

        return element;
    }

    private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatAmount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatQuantity(decimal value) =>
        value.ToString("0.00####", CultureInfo.InvariantCulture);

    private sealed class Utf8StringWriter : StringWriter
    {
        public Utf8StringWriter(StringBuilder builder) : base(builder, CultureInfo.InvariantCulture)
        {
        }

        public override Encoding Encoding => Encoding.UTF8;
    }
}
