using System.Xml.Linq;
using ITBees.Ksef.Configuration;
using ITBees.Ksef.Invoicing;
using Microsoft.Extensions.Options;
using Xunit;

namespace ITBees.Ksef.Tests;

public class Fa3XmlGeneratorTests
{
    private static readonly XNamespace Ns = "http://crd.gov.pl/wzor/2025/06/25/13775/";

    private static Fa3XmlGenerator CreateGenerator(KsefSellerOptions? seller = null) =>
        new(Options.Create(new KsefOptions
        {
            SystemInfo = "UnitTests",
            Seller = seller ?? new KsefSellerOptions
            {
                Nip = "5555555555",
                Name = "Sprzedawca Sp. z o.o.",
                AddressLine1 = "ul. Testowa 1",
                AddressLine2 = "00-001 Warszawa"
            }
        }));

    private static KsefInvoice CreateInvoice() => new()
    {
        Number = "FV/2026/08/001",
        IssueDate = new DateOnly(2026, 8, 1),
        SaleDate = new DateOnly(2026, 8, 1),
        Currency = "PLN",
        Buyer = new KsefParty
        {
            Nip = "1111111111",
            Name = "Nabywca S.A.",
            AddressLine1 = "ul. Polna 2",
            AddressLine2 = "11-111 Kraków"
        },
        Lines =
        {
            new KsefInvoiceLine { Name = "Abonament", Quantity = 1, UnitNetPrice = 100m, VatRate = 23 }
        },
        IsPaid = true,
        PaymentDate = new DateOnly(2026, 8, 1)
    };

    [Fact]
    public void Generate_ProducesFa3HeaderWithCorrectNamespaceAndFormCode()
    {
        var xml = CreateGenerator().Generate(CreateInvoice(), new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero));
        var doc = XDocument.Parse(xml);

        Assert.Equal(Ns + "Faktura", doc.Root!.Name);
        var kodFormularza = doc.Root.Element(Ns + "Naglowek")!.Element(Ns + "KodFormularza")!;
        Assert.Equal("FA", kodFormularza.Value);
        Assert.Equal("FA (3)", kodFormularza.Attribute("kodSystemowy")!.Value);
        Assert.Equal("1-0E", kodFormularza.Attribute("wersjaSchemy")!.Value);
        Assert.Equal("3", doc.Root.Element(Ns + "Naglowek")!.Element(Ns + "WariantFormularza")!.Value);
        Assert.Equal("2026-08-01T12:00:00Z",
            doc.Root.Element(Ns + "Naglowek")!.Element(Ns + "DataWytworzeniaFa")!.Value);
    }

    [Fact]
    public void Generate_ComputesVatAggregatesAndTotal()
    {
        var invoice = CreateInvoice();
        invoice.Lines.Add(new KsefInvoiceLine { Name = "Usługa 8%", Quantity = 2, UnitNetPrice = 50m, VatRate = 8 });

        var xml = CreateGenerator().Generate(invoice);
        var fa = XDocument.Parse(xml).Root!.Element(Ns + "Fa")!;

        Assert.Equal("100.00", fa.Element(Ns + "P_13_1")!.Value);
        Assert.Equal("23.00", fa.Element(Ns + "P_14_1")!.Value);
        Assert.Equal("100.00", fa.Element(Ns + "P_13_2")!.Value);
        Assert.Equal("8.00", fa.Element(Ns + "P_14_2")!.Value);
        // 100 + 23 + 100 + 8
        Assert.Equal("231.00", fa.Element(Ns + "P_15")!.Value);
    }

    [Fact]
    public void Generate_RendersInvoiceLines()
    {
        var xml = CreateGenerator().Generate(CreateInvoice());
        var line = XDocument.Parse(xml).Root!.Element(Ns + "Fa")!.Element(Ns + "FaWiersz")!;

        Assert.Equal("1", line.Element(Ns + "NrWierszaFa")!.Value);
        Assert.Equal("Abonament", line.Element(Ns + "P_7")!.Value);
        Assert.Equal("1.00", line.Element(Ns + "P_8B")!.Value);
        Assert.Equal("100.00", line.Element(Ns + "P_9A")!.Value);
        Assert.Equal("100.00", line.Element(Ns + "P_11")!.Value);
        Assert.Equal("23", line.Element(Ns + "P_12")!.Value);
    }

    [Fact]
    public void Generate_UsesSellerFromOptionsWhenInvoiceHasNone()
    {
        var xml = CreateGenerator().Generate(CreateInvoice());
        var podmiot1 = XDocument.Parse(xml).Root!.Element(Ns + "Podmiot1")!;

        Assert.Equal("5555555555", podmiot1.Element(Ns + "DaneIdentyfikacyjne")!.Element(Ns + "NIP")!.Value);
        Assert.Equal("Sprzedawca Sp. z o.o.",
            podmiot1.Element(Ns + "DaneIdentyfikacyjne")!.Element(Ns + "Nazwa")!.Value);
    }

    [Fact]
    public void Generate_EmitsBrakIdForConsumerBuyer()
    {
        var invoice = CreateInvoice();
        invoice.Buyer.Nip = null;

        var xml = CreateGenerator().Generate(invoice);
        var identification = XDocument.Parse(xml).Root!.Element(Ns + "Podmiot2")!
            .Element(Ns + "DaneIdentyfikacyjne")!;

        Assert.Null(identification.Element(Ns + "NIP"));
        Assert.Equal("1", identification.Element(Ns + "BrakID")!.Value);
    }

    [Fact]
    public void Generate_EmitsPaymentSectionForPaidInvoice()
    {
        var xml = CreateGenerator().Generate(CreateInvoice());
        var platnosc = XDocument.Parse(xml).Root!.Element(Ns + "Fa")!.Element(Ns + "Platnosc")!;

        Assert.Equal("1", platnosc.Element(Ns + "Zaplacono")!.Value);
        Assert.Equal("2026-08-01", platnosc.Element(Ns + "DataZaplaty")!.Value);
    }

    [Fact]
    public void Generate_ThrowsForUnsupportedVatRate()
    {
        var invoice = CreateInvoice();
        invoice.Lines[0].VatRate = 19;

        Assert.Throws<NotSupportedException>(() => CreateGenerator().Generate(invoice));
    }

    [Fact]
    public void Generate_ThrowsWhenSellerMissingEverywhere()
    {
        var generator = new Fa3XmlGenerator(Options.Create(new KsefOptions()));
        Assert.Throws<InvalidOperationException>(() => generator.Generate(CreateInvoice()));
    }

    [Fact]
    public void Generate_ThrowsWhenNoLines()
    {
        var invoice = CreateInvoice();
        invoice.Lines.Clear();
        Assert.Throws<ArgumentException>(() => CreateGenerator().Generate(invoice));
    }
}
