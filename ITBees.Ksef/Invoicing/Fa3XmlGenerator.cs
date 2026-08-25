using System.Globalization;
using System.Text;
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
        Dictionary<int, (decimal Net, decimal Vat)> vatTotals;
        if (invoice.Advance != null)
        {
            vatTotals = invoice.Advance.Payments
                .GroupBy(p => p.VatRate)
                .ToDictionary(g => g.Key, g =>
                {
                    var gross = g.Sum(p => p.GrossAmount);
                    var vat = Math.Round(gross * g.Key / (100m + g.Key), 2, MidpointRounding.AwayFromZero);
                    return (gross - vat, vat);
                });
        }
        else
        {
            vatTotals = invoice.Lines
                .GroupBy(l => l.VatRate)
                .ToDictionary(g => g.Key, g =>
                {
                    var net = g.Sum(l => l.StateBeforeCorrection ? -l.GetNetValue() : l.GetNetValue());
                    return (net, Math.Round(net * g.Key / 100m, 2, MidpointRounding.AwayFromZero));
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
        fa.Add(BuildAdnotacje(ns));
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
                new XElement(ns + "P_12", line.VatRate.ToString(CultureInfo.InvariantCulture)));

            // StanPrzed closes the row sequence in the schema, so it has to be added last.
            if (line.StateBeforeCorrection)
                row.Add(new XElement(ns + "StanPrzed", 1));

            fa.Add(row);
        }

        if (invoice.IsPaid)
        {
            fa.Add(new XElement(ns + "Platnosc",
                new XElement(ns + "Zaplacono", "1"),
                new XElement(ns + "DataZaplaty", FormatDate(invoice.PaymentDate ?? invoice.IssueDate))));
        }

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

        var rates = invoice.Lines.Select(l => l.VatRate)
            .Concat(invoice.Advance?.Payments.Select(p => p.VatRate) ?? Enumerable.Empty<int>())
            .Concat(invoice.Advance?.OrderLines.Select(l => l.VatRate) ?? Enumerable.Empty<int>());
        var unsupportedRate = rates.Where(r => r is not (23 or 22 or 8 or 7 or 5 or 4 or 0))
            .Cast<int?>().FirstOrDefault();
        if (unsupportedRate != null)
            throw new NotSupportedException(
                $"VAT rate {unsupportedRate}% is not supported by this generator (supported: 23, 22, 8, 7, 5, 4, 0).");

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
            element.Add(new XElement(ns + "ZamowienieWiersz",
                new XElement(ns + "NrWierszaZam", rowNumber),
                new XElement(ns + "P_7Z", line.Name),
                new XElement(ns + "P_8AZ", line.Unit),
                new XElement(ns + "P_8BZ", FormatQuantity(line.Quantity)),
                new XElement(ns + "P_9AZ", FormatAmount(line.UnitNetPrice)),
                new XElement(ns + "P_11NettoZ", FormatAmount(net)),
                new XElement(ns + "P_11VatZ",
                    FormatAmount(Math.Round(net * line.VatRate / 100m, 2, MidpointRounding.AwayFromZero))),
                new XElement(ns + "P_12Z", line.VatRate.ToString(CultureInfo.InvariantCulture))));
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

    /// <summary>Maps per-rate net and tax totals to FA(3) aggregate fields: 23/22% → P_13_1/P_14_1, 8/7% → P_13_2/P_14_2, 5/4% → P_13_3/P_14_3, 0% (domestic) → P_13_6_1.</summary>
    private static void AppendVatAggregates(XElement fa, XNamespace ns,
        Dictionary<int, (decimal Net, decimal Vat)> vatTotals)
    {
        void Append(int[] rates, string netField, string? vatField)
        {
            if (!rates.Any(vatTotals.ContainsKey))
                return;

            fa.Add(new XElement(ns + netField,
                FormatAmount(rates.Where(vatTotals.ContainsKey).Sum(r => vatTotals[r].Net))));
            if (vatField != null)
                fa.Add(new XElement(ns + vatField,
                    FormatAmount(rates.Where(vatTotals.ContainsKey).Sum(r => vatTotals[r].Vat))));
        }

        Append(new[] { 23, 22 }, "P_13_1", "P_14_1");
        Append(new[] { 8, 7 }, "P_13_2", "P_14_2");
        Append(new[] { 5, 4 }, "P_13_3", "P_14_3");
        Append(new[] { 0 }, "P_13_6_1", null);
    }

    private static XElement BuildAdnotacje(XNamespace ns) =>
        // Standard "not applicable" markers (2 = no) for an ordinary domestic VAT invoice.
        new(ns + "Adnotacje",
            new XElement(ns + "P_16", 2),
            new XElement(ns + "P_17", 2),
            new XElement(ns + "P_18", 2),
            new XElement(ns + "P_18A", 2),
            new XElement(ns + "Zwolnienie", new XElement(ns + "P_19N", 1)),
            new XElement(ns + "NoweSrodkiTransportu", new XElement(ns + "P_22N", 1)),
            new XElement(ns + "P_23", 2),
            new XElement(ns + "PMarzy", new XElement(ns + "P_PMarzyN", 1)));

    private static XElement BuildParty(XNamespace ns, string elementName, KsefParty party)
    {
        var identification = new XElement(ns + "DaneIdentyfikacyjne");
        if (!string.IsNullOrWhiteSpace(party.Nip))
            identification.Add(new XElement(ns + "NIP", party.Nip));
        else if (elementName == "Podmiot2")
            identification.Add(new XElement(ns + "BrakID", 1));
        else
            throw new ArgumentException("Seller (Podmiot1) must have a NIP.");
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
