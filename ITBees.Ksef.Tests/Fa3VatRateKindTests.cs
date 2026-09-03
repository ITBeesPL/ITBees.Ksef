using System.Xml.Linq;
using ITBees.Ksef.Configuration;
using ITBees.Ksef.Invoicing;
using Microsoft.Extensions.Options;
using Xunit;

namespace ITBees.Ksef.Tests;

/// <summary>
/// P_12 is an enumeration in FA(3) (TStawkaPodatku): a bare "0" is rejected by KSeF with
/// "Enumeration constraint failed", and every untaxed case has its own aggregate field.
/// </summary>
public class Fa3VatRateKindTests
{
    private static readonly XNamespace Ns = "http://crd.gov.pl/wzor/2025/06/25/13775/";

    private static Fa3XmlGenerator CreateGenerator() =>
        new(Options.Create(new KsefOptions
        {
            SystemInfo = "UnitTests",
            Seller = new KsefSellerOptions
            {
                Nip = "5555555555",
                Name = "Sprzedawca Sp. z o.o.",
                AddressLine1 = "ul. Testowa 1",
                AddressLine2 = "00-001 Warszawa"
            }
        }));

    private static KsefInvoice CreateInvoice(params KsefInvoiceLine[] lines) => new()
    {
        Number = "FV/2026/09/002",
        IssueDate = new DateOnly(2026, 9, 1),
        SaleDate = new DateOnly(2026, 8, 31),
        Currency = "PLN",
        Buyer = new KsefParty
        {
            Nip = "1111111111",
            Name = "Nabywca S.A.",
            AddressLine1 = "ul. Polna 2",
            AddressLine2 = "11-111 Kraków"
        },
        Lines = lines.ToList()
    };

    private static XElement Fa(string xml) => XDocument.Parse(xml).Root!.Element(Ns + "Fa")!;

    [Theory]
    [InlineData(23, KsefVatRateKind.Standard, "23")]
    [InlineData(8, KsefVatRateKind.Standard, "8")]
    [InlineData(0, KsefVatRateKind.Standard, "0 KR")]
    [InlineData(0, KsefVatRateKind.ZeroIntraCommunity, "0 WDT")]
    [InlineData(0, KsefVatRateKind.ZeroExport, "0 EX")]
    [InlineData(0, KsefVatRateKind.Exempt, "zw")]
    [InlineData(0, KsefVatRateKind.ReverseCharge, "oo")]
    [InlineData(0, KsefVatRateKind.NotSubjectNonEu, "np I")]
    [InlineData(0, KsefVatRateKind.NotSubjectEu, "np II")]
    public void P12_UsesTheSchemaCodeForEachKind(int rate, KsefVatRateKind kind, string expected)
    {
        var invoice = CreateInvoice(new KsefInvoiceLine
        {
            Name = "Pozycja", Quantity = 1, UnitNetPrice = 100m, VatRate = rate, VatRateKind = kind
        });
        invoice.ExemptionBasis = "art. 43 ust. 1 pkt 19 ustawy o VAT";

        var fa = Fa(CreateGenerator().Generate(invoice));

        Assert.Equal(expected, fa.Element(Ns + "FaWiersz")!.Element(Ns + "P_12")!.Value);
    }

    [Theory]
    [InlineData(0, KsefVatRateKind.Standard, "P_13_6_1")]
    [InlineData(0, KsefVatRateKind.ZeroIntraCommunity, "P_13_6_2")]
    [InlineData(0, KsefVatRateKind.ZeroExport, "P_13_6_3")]
    [InlineData(0, KsefVatRateKind.Exempt, "P_13_7")]
    [InlineData(0, KsefVatRateKind.NotSubjectNonEu, "P_13_8")]
    [InlineData(0, KsefVatRateKind.NotSubjectEu, "P_13_9")]
    [InlineData(0, KsefVatRateKind.ReverseCharge, "P_13_10")]
    public void UntaxedKinds_LandInTheirOwnAggregateWithoutTax(int rate, KsefVatRateKind kind, string field)
    {
        var invoice = CreateInvoice(new KsefInvoiceLine
        {
            Name = "Development services", Quantity = 1, UnitNetPrice = 22916.66m, VatRate = rate, VatRateKind = kind
        });
        invoice.ExemptionBasis = "art. 113 ust. 1 ustawy o VAT";

        var fa = Fa(CreateGenerator().Generate(invoice));

        Assert.Equal("22916.66", fa.Element(Ns + field)!.Value);
        Assert.Equal("22916.66", fa.Element(Ns + "P_15")!.Value);
        Assert.Null(fa.Element(Ns + "P_14_1"));
        Assert.Null(fa.Element(Ns + "P_13_1"));
        // The same 0% amount must not leak into the domestic 0% field of another kind.
        foreach (var other in new[] { "P_13_6_1", "P_13_6_2", "P_13_6_3", "P_13_7", "P_13_8", "P_13_9", "P_13_10" }
                     .Where(x => x != field))
            Assert.Null(fa.Element(Ns + other));
    }

    [Fact]
    public void TwoZeroRateKinds_AreNotMergedIntoOneAggregate()
    {
        var invoice = CreateInvoice(
            new KsefInvoiceLine { Name = "Krajowa 0%", Quantity = 1, UnitNetPrice = 100m, VatRate = 0 },
            new KsefInvoiceLine
            {
                Name = "Eksport", Quantity = 1, UnitNetPrice = 200m, VatRate = 0, VatRateKind = KsefVatRateKind.ZeroExport
            },
            new KsefInvoiceLine { Name = "Krajowa 23%", Quantity = 1, UnitNetPrice = 50m, VatRate = 23 });

        var fa = Fa(CreateGenerator().Generate(invoice));

        Assert.Equal("100.00", fa.Element(Ns + "P_13_6_1")!.Value);
        Assert.Equal("200.00", fa.Element(Ns + "P_13_6_3")!.Value);
        Assert.Equal("50.00", fa.Element(Ns + "P_13_1")!.Value);
        Assert.Equal("11.50", fa.Element(Ns + "P_14_1")!.Value);
        Assert.Equal("361.50", fa.Element(Ns + "P_15")!.Value);
    }

    [Fact]
    public void ReverseCharge_SetsP18()
    {
        var invoice = CreateInvoice(new KsefInvoiceLine
        {
            Name = "Roboty budowlane", Quantity = 1, UnitNetPrice = 1000m, VatRate = 0,
            VatRateKind = KsefVatRateKind.ReverseCharge
        });

        var adnotacje = Fa(CreateGenerator().Generate(invoice)).Element(Ns + "Adnotacje")!;

        Assert.Equal("1", adnotacje.Element(Ns + "P_18")!.Value);
        Assert.Equal("1", adnotacje.Element(Ns + "Zwolnienie")!.Element(Ns + "P_19N")!.Value);
    }

    [Fact]
    public void Exempt_EmitsP19WithBasisInsteadOfP19N()
    {
        var invoice = CreateInvoice(new KsefInvoiceLine
        {
            Name = "Usługa medyczna", Quantity = 1, UnitNetPrice = 500m, VatRate = 0, VatRateKind = KsefVatRateKind.Exempt
        });
        invoice.ExemptionBasis = "art. 43 ust. 1 pkt 19 ustawy o VAT";

        var zwolnienie = Fa(CreateGenerator().Generate(invoice)).Element(Ns + "Adnotacje")!.Element(Ns + "Zwolnienie")!;

        Assert.Equal("1", zwolnienie.Element(Ns + "P_19")!.Value);
        Assert.Equal("art. 43 ust. 1 pkt 19 ustawy o VAT", zwolnienie.Element(Ns + "P_19A")!.Value);
        Assert.Null(zwolnienie.Element(Ns + "P_19N"));
    }

    [Fact]
    public void Exempt_WithoutBasis_IsRejectedBeforeKsefWould()
    {
        var invoice = CreateInvoice(new KsefInvoiceLine
        {
            Name = "Usługa medyczna", Quantity = 1, UnitNetPrice = 500m, VatRate = 0, VatRateKind = KsefVatRateKind.Exempt
        });

        var ex = Assert.Throws<ArgumentException>(() => CreateGenerator().Generate(invoice));
        Assert.Contains("ExemptionBasis", ex.Message);
    }

    [Fact]
    public void NonStandardKind_WithPercentage_IsRejected()
    {
        var invoice = CreateInvoice(new KsefInvoiceLine
        {
            Name = "Pozycja", Quantity = 1, UnitNetPrice = 100m, VatRate = 23, VatRateKind = KsefVatRateKind.NotSubjectNonEu
        });

        var ex = Assert.Throws<ArgumentException>(() => CreateGenerator().Generate(invoice));
        Assert.Contains("VatRate = 0", ex.Message);
    }

    [Fact]
    public void AdvancePayment_UntaxedKind_HasNoTaxAndGoesToItsAggregate()
    {
        var invoice = new KsefInvoice
        {
            Number = "FZ/2026/09/001",
            IssueDate = new DateOnly(2026, 9, 1),
            Currency = "PLN",
            Buyer = new KsefParty { Nip = "1111111111", Name = "Nabywca S.A.", AddressLine1 = "ul. Polna 2", AddressLine2 = "11-111 Kraków" },
            Advance = new KsefAdvance
            {
                Payments =
                {
                    new KsefAdvancePayment { VatRate = 0, VatRateKind = KsefVatRateKind.NotSubjectNonEu, GrossAmount = 1000m }
                },
                OrderGrossTotal = 5000m,
                OrderLines =
                {
                    new KsefInvoiceLine
                    {
                        Name = "Wdrożenie", Quantity = 1, UnitNetPrice = 5000m, VatRate = 0,
                        VatRateKind = KsefVatRateKind.NotSubjectNonEu
                    }
                }
            }
        };

        var fa = Fa(CreateGenerator().Generate(invoice));

        Assert.Equal("1000.00", fa.Element(Ns + "P_13_8")!.Value);
        Assert.Equal("1000.00", fa.Element(Ns + "P_15")!.Value);
        var row = fa.Element(Ns + "Zamowienie")!.Element(Ns + "ZamowienieWiersz")!;
        Assert.Equal("0.00", row.Element(Ns + "P_11VatZ")!.Value);
        Assert.Equal("np I", row.Element(Ns + "P_12Z")!.Value);
    }

    [Fact]
    public void Buyer_OutsideEu_IsIdentifiedByKodKrajuAndNrID()
    {
        var invoice = CreateInvoice(new KsefInvoiceLine
        {
            Name = "Development services", Quantity = 1, UnitNetPrice = 100m, VatRate = 0,
            VatRateKind = KsefVatRateKind.NotSubjectNonEu
        });
        invoice.Buyer = new KsefParty
        {
            Name = "Wireless Logic Limited",
            ForeignTaxId = "2090006259",
            CountryCode = "GB",
            AddressLine1 = "Horizon, Hurley",
            AddressLine2 = "SL6 6RJ Berkshire"
        };

        var id = XDocument.Parse(CreateGenerator().Generate(invoice)).Root!
            .Element(Ns + "Podmiot2")!.Element(Ns + "DaneIdentyfikacyjne")!;

        Assert.Null(id.Element(Ns + "NIP"));
        Assert.Null(id.Element(Ns + "BrakID"));
        Assert.Equal("GB", id.Element(Ns + "KodKraju")!.Value);
        Assert.Equal("2090006259", id.Element(Ns + "NrID")!.Value);
    }

    [Fact]
    public void Buyer_InEu_IsIdentifiedByKodUEAndNrVatUE()
    {
        var invoice = CreateInvoice(new KsefInvoiceLine
        {
            Name = "Usługa", Quantity = 1, UnitNetPrice = 100m, VatRate = 0, VatRateKind = KsefVatRateKind.NotSubjectEu
        });
        invoice.Buyer = new KsefParty
        {
            Name = "Beispiel GmbH",
            EuVatNumber = "123456789",
            CountryCode = "DE",
            AddressLine1 = "Musterstraße 1",
            AddressLine2 = "10115 Berlin"
        };

        var id = XDocument.Parse(CreateGenerator().Generate(invoice)).Root!
            .Element(Ns + "Podmiot2")!.Element(Ns + "DaneIdentyfikacyjne")!;

        Assert.Equal("DE", id.Element(Ns + "KodUE")!.Value);
        Assert.Equal("123456789", id.Element(Ns + "NrVatUE")!.Value);
        Assert.Null(id.Element(Ns + "NrID"));
    }

    [Fact]
    public void Buyer_GreekIsoCode_BecomesElPrefix()
    {
        var invoice = CreateInvoice(new KsefInvoiceLine { Name = "Usługa", Quantity = 1, UnitNetPrice = 100m, VatRate = 23 });
        invoice.Buyer = new KsefParty
        {
            Name = "Παράδειγμα ΑΕ", EuVatNumber = "123456789", CountryCode = "GR",
            AddressLine1 = "Odos 1", AddressLine2 = "10431 Athina"
        };

        var id = XDocument.Parse(CreateGenerator().Generate(invoice)).Root!
            .Element(Ns + "Podmiot2")!.Element(Ns + "DaneIdentyfikacyjne")!;

        Assert.Equal("EL", id.Element(Ns + "KodUE")!.Value);
    }

    [Fact]
    public void Buyer_EuVatNumberWithNonEuPrefix_IsRejected()
    {
        var invoice = CreateInvoice(new KsefInvoiceLine { Name = "Usługa", Quantity = 1, UnitNetPrice = 100m, VatRate = 23 });
        invoice.Buyer = new KsefParty
        {
            Name = "Wireless Logic Limited", EuVatNumber = "2090006259", CountryCode = "GB",
            AddressLine1 = "Horizon", AddressLine2 = "SL6 6RJ"
        };

        var ex = Assert.Throws<ArgumentException>(() => CreateGenerator().Generate(invoice));
        Assert.Contains("GB", ex.Message);
    }

    [Theory]
    [InlineData("23", 23, KsefVatRateKind.Standard)]
    [InlineData("0 KR", 0, KsefVatRateKind.Standard)]
    [InlineData("0 WDT", 0, KsefVatRateKind.ZeroIntraCommunity)]
    [InlineData("0 EX", 0, KsefVatRateKind.ZeroExport)]
    [InlineData("zw", 0, KsefVatRateKind.Exempt)]
    [InlineData("oo", 0, KsefVatRateKind.ReverseCharge)]
    [InlineData("np I", 0, KsefVatRateKind.NotSubjectNonEu)]
    [InlineData("np II", 0, KsefVatRateKind.NotSubjectEu)]
    [InlineData("np", 0, KsefVatRateKind.NotSubjectNonEu)]
    [InlineData("8%", 8, KsefVatRateKind.Standard)]
    [InlineData("", 0, KsefVatRateKind.Standard)]
    [InlineData(null, 0, KsefVatRateKind.Standard)]
    public void Parse_ReadsP12BackIntoRateAndKind(string? p12, int rate, KsefVatRateKind kind)
    {
        Assert.Equal((rate, kind), KsefVatRates.Parse(p12));
    }
}
