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
    public void Generate_EmitsDueDateAndBankAccountForUnpaidInvoice()
    {
        var invoice = CreateInvoice();
        invoice.IsPaid = false;
        invoice.PaymentDate = null;
        invoice.PaymentDueDate = new DateOnly(2026, 9, 1);
        invoice.BankAccount = new KsefBankAccount
        {
            Number = "44 1140 2004 0000 3402 8563 8379",
            Description = "Rachunek firmowy"
        };

        var platnosc = XDocument.Parse(CreateGenerator().Generate(invoice))
            .Root!.Element(Ns + "Fa")!.Element(Ns + "Platnosc")!;

        Assert.Null(platnosc.Element(Ns + "Zaplacono"));
        Assert.Equal("2026-09-01", platnosc.Element(Ns + "TerminPlatnosci")!.Element(Ns + "Termin")!.Value);
        var account = platnosc.Element(Ns + "RachunekBankowy")!;
        Assert.Equal("44114020040000340285638379", account.Element(Ns + "NrRB")!.Value);
        Assert.Equal("Rachunek firmowy", account.Element(Ns + "OpisRachunku")!.Value);
    }

    [Fact]
    public void Generate_OmitsPaymentSectionWhenThereIsNothingToSay()
    {
        var invoice = CreateInvoice();
        invoice.IsPaid = false;
        invoice.PaymentDate = null;

        var fa = XDocument.Parse(CreateGenerator().Generate(invoice)).Root!.Element(Ns + "Fa")!;

        Assert.Null(fa.Element(Ns + "Platnosc"));
    }

    [Fact]
    public void Generate_EmitsDueDateAfterPaidMarkerOnPaidInvoice()
    {
        var invoice = CreateInvoice();
        invoice.PaymentDueDate = new DateOnly(2026, 9, 1);

        var platnosc = XDocument.Parse(CreateGenerator().Generate(invoice))
            .Root!.Element(Ns + "Fa")!.Element(Ns + "Platnosc")!;

        // Schema order inside Platnosc: Zaplacono/DataZaplaty first, TerminPlatnosci after.
        Assert.Equal(new[] { "Zaplacono", "DataZaplaty", "TerminPlatnosci" },
            platnosc.Elements().Select(x => x.Name.LocalName).ToArray());
    }

    [Fact]
    public void Generate_ThrowsForBankAccountNumberOutsideSchemaLength()
    {
        var invoice = CreateInvoice();
        invoice.BankAccount = new KsefBankAccount { Number = "123 456" };

        Assert.Throws<ArgumentException>(() => CreateGenerator().Generate(invoice));
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

    /// <summary>Original: 10 h × 250 PLN at 23%. Correction: 8 h — 500 PLN net less.</summary>
    private static KsefInvoice CreateCorrection() => new()
    {
        Number = "KOR/2026/08/001",
        IssueDate = new DateOnly(2026, 8, 20),
        SaleDate = new DateOnly(2026, 8, 20),
        Currency = "PLN",
        Buyer = new KsefParty
        {
            Nip = "1111111111",
            Name = "Nabywca S.A.",
            AddressLine1 = "ul. Polna 2",
            AddressLine2 = "11-111 Kraków"
        },
        Correction = new KsefInvoiceCorrection
        {
            Reason = "Błąd w cenie",
            Type = KsefCorrectionType.OriginalPeriod,
            CorrectedNumber = "FV/2026/08/001",
            CorrectedIssueDate = new DateOnly(2026, 8, 1),
            CorrectedKsefNumber = "1111111111-20260801-0100A1B2C3D4-45"
        },
        Lines =
        {
            new KsefInvoiceLine
            {
                Name = "Konsultacje IT", Unit = "h", Quantity = 10, UnitNetPrice = 250m, VatRate = 23,
                StateBeforeCorrection = true
            },
            new KsefInvoiceLine { Name = "Konsultacje IT", Unit = "h", Quantity = 8, UnitNetPrice = 250m, VatRate = 23 }
        }
    };

    [Fact]
    public void Generate_MarksCorrectionAsKorAndDescribesCorrectedInvoice()
    {
        var xml = CreateGenerator().Generate(CreateCorrection());
        var fa = XDocument.Parse(xml).Root!.Element(Ns + "Fa")!;

        Assert.Equal("KOR", fa.Element(Ns + "RodzajFaktury")!.Value);
        Assert.Equal("Błąd w cenie", fa.Element(Ns + "PrzyczynaKorekty")!.Value);
        Assert.Equal("1", fa.Element(Ns + "TypKorekty")!.Value);

        var corrected = fa.Element(Ns + "DaneFaKorygowanej")!;
        Assert.Equal("2026-08-01", corrected.Element(Ns + "DataWystFaKorygowanej")!.Value);
        Assert.Equal("FV/2026/08/001", corrected.Element(Ns + "NrFaKorygowanej")!.Value);
        Assert.Equal("1", corrected.Element(Ns + "NrKSeF")!.Value);
        Assert.Equal("1111111111-20260801-0100A1B2C3D4-45",
            corrected.Element(Ns + "NrKSeFFaKorygowanej")!.Value);
        Assert.Null(corrected.Element(Ns + "NrKSeFN"));
    }

    [Fact]
    public void Generate_MarksInvoiceCorrectedOutsideKsefWithNrKSeFN()
    {
        var invoice = CreateCorrection();
        invoice.Correction!.CorrectedKsefNumber = null;

        var corrected = XDocument.Parse(CreateGenerator().Generate(invoice)).Root!
            .Element(Ns + "Fa")!.Element(Ns + "DaneFaKorygowanej")!;

        Assert.Equal("1", corrected.Element(Ns + "NrKSeFN")!.Value);
        Assert.Null(corrected.Element(Ns + "NrKSeF"));
        Assert.Null(corrected.Element(Ns + "NrKSeFFaKorygowanej"));
    }

    [Fact]
    public void Generate_FlagsRowsWithTheStateBeforeCorrection()
    {
        var xml = CreateGenerator().Generate(CreateCorrection());
        var rows = XDocument.Parse(xml).Root!.Element(Ns + "Fa")!.Elements(Ns + "FaWiersz").ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal("1", rows[0].Element(Ns + "NrWierszaFa")!.Value);
        Assert.Equal("1", rows[0].Element(Ns + "StanPrzed")!.Value);
        Assert.Equal("2", rows[1].Element(Ns + "NrWierszaFa")!.Value);
        Assert.Null(rows[1].Element(Ns + "StanPrzed"));
    }

    [Fact]
    public void Generate_ReportsCorrectionAggregatesAsTheDifference()
    {
        var fa = XDocument.Parse(CreateGenerator().Generate(CreateCorrection())).Root!.Element(Ns + "Fa")!;

        // 2000 po korekcie − 2500 przed korektą
        Assert.Equal("-500.00", fa.Element(Ns + "P_13_1")!.Value);
        Assert.Equal("-115.00", fa.Element(Ns + "P_14_1")!.Value);
        Assert.Equal("-615.00", fa.Element(Ns + "P_15")!.Value);
    }

    /// <summary>Correcting an invoice down to zero leaves only the "before" rows.</summary>
    [Fact]
    public void Generate_ReversesWholeInvoiceWhenNoRowsRemainAfterCorrection()
    {
        var invoice = CreateCorrection();
        invoice.Lines.RemoveAll(l => !l.StateBeforeCorrection);

        var fa = XDocument.Parse(CreateGenerator().Generate(invoice)).Root!.Element(Ns + "Fa")!;

        Assert.Equal("-2500.00", fa.Element(Ns + "P_13_1")!.Value);
        Assert.Equal("-575.00", fa.Element(Ns + "P_14_1")!.Value);
        Assert.Equal("-3075.00", fa.Element(Ns + "P_15")!.Value);
    }

    [Fact]
    public void Generate_ThrowsWhenCorrectionHasNoReason()
    {
        var invoice = CreateCorrection();
        invoice.Correction!.Reason = "   ";

        Assert.Throws<ArgumentException>(() => CreateGenerator().Generate(invoice));
    }

    [Fact]
    public void Generate_ThrowsWhenCorrectedInvoiceIsNotIdentified()
    {
        var invoice = CreateCorrection();
        invoice.Correction!.CorrectedNumber = "";

        Assert.Throws<ArgumentException>(() => CreateGenerator().Generate(invoice));
    }

    [Fact]
    public void Generate_ThrowsWhenStateBeforeCorrectionIsUsedOnPlainInvoice()
    {
        var invoice = CreateInvoice();
        invoice.Lines[0].StateBeforeCorrection = true;

        Assert.Throws<ArgumentException>(() => CreateGenerator().Generate(invoice));
    }

    [Fact]
    public void Generate_EmitsNotesAsInvoiceFooter()
    {
        var invoice = CreateInvoice();
        invoice.Notes = "Zamówienie nr 44/2026.\nTowar odebrano osobiście.";

        var root = XDocument.Parse(CreateGenerator().Generate(invoice)).Root!;
        var stopka = root.Element(Ns + "Stopka")!;

        Assert.Equal("Zamówienie nr 44/2026.\nTowar odebrano osobiście.",
            stopka.Element(Ns + "Informacje")!.Element(Ns + "StopkaFaktury")!.Value);
        // Stopka follows Fa in the schema sequence.
        Assert.Equal(Ns + "Fa", (stopka.PreviousNode as XElement)!.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Generate_OmitsStopkaWhenNotesAreBlank(string? notes)
    {
        var invoice = CreateInvoice();
        invoice.Notes = notes;

        var root = XDocument.Parse(CreateGenerator().Generate(invoice)).Root!;

        Assert.Null(root.Element(Ns + "Stopka"));
    }

    [Fact]
    public void Generate_ThrowsWhenNotesExceedStopkaFakturyLimit()
    {
        var invoice = CreateInvoice();
        invoice.Notes = new string('a', 3501);

        Assert.Throws<ArgumentException>(() => CreateGenerator().Generate(invoice));
    }

    /// <summary>Advance invoice: 1230 gross received at 23% and 270 gross at 8% against a 2310 order.</summary>
    private static KsefInvoice CreateAdvance() => new()
    {
        Number = "ZAL/2026/08/001",
        IssueDate = new DateOnly(2026, 8, 20),
        // On an advance invoice P_6 is the date the payment was received.
        SaleDate = new DateOnly(2026, 8, 18),
        Currency = "PLN",
        Buyer = new KsefParty
        {
            Nip = "1111111111",
            Name = "Nabywca S.A.",
            AddressLine1 = "ul. Polna 2",
            AddressLine2 = "11-111 Kraków"
        },
        Advance = new KsefAdvance
        {
            Payments =
            {
                new KsefAdvancePayment { VatRate = 23, GrossAmount = 1230m },
                new KsefAdvancePayment { VatRate = 8, GrossAmount = 270m }
            },
            OrderGrossTotal = 2310m,
            OrderLines =
            {
                new KsefInvoiceLine { Name = "Sprzęt", Unit = "szt.", Quantity = 1, UnitNetPrice = 1000m, VatRate = 23 },
                new KsefInvoiceLine { Name = "Montaż", Unit = "usł.", Quantity = 1, UnitNetPrice = 1000m, VatRate = 8 }
            }
        },
        IsPaid = true,
        PaymentDate = new DateOnly(2026, 8, 18)
    };

    [Fact]
    public void Generate_MarksAdvanceAsZalAndComputesTaxFromReceivedPayment()
    {
        var fa = XDocument.Parse(CreateGenerator().Generate(CreateAdvance())).Root!.Element(Ns + "Fa")!;

        Assert.Equal("ZAL", fa.Element(Ns + "RodzajFaktury")!.Value);
        // KP = ZB × SP / (100 + SP): 1230 → 230 (23%), 270 → 20 (8%); net is the remainder.
        Assert.Equal("1000.00", fa.Element(Ns + "P_13_1")!.Value);
        Assert.Equal("230.00", fa.Element(Ns + "P_14_1")!.Value);
        Assert.Equal("250.00", fa.Element(Ns + "P_13_2")!.Value);
        Assert.Equal("20.00", fa.Element(Ns + "P_14_2")!.Value);
        // P_15 is the received payment, not the order value.
        Assert.Equal("1500.00", fa.Element(Ns + "P_15")!.Value);
        Assert.Equal("2026-08-18", fa.Element(Ns + "P_6")!.Value);
        // The order goes into Zamowienie — an advance invoice has no FaWiersz rows.
        Assert.Empty(fa.Elements(Ns + "FaWiersz"));
    }

    [Fact]
    public void Generate_DescribesTheOrderInZamowienieNode()
    {
        var fa = XDocument.Parse(CreateGenerator().Generate(CreateAdvance())).Root!.Element(Ns + "Fa")!;
        var zamowienie = fa.Element(Ns + "Zamowienie")!;

        Assert.Equal("2310.00", zamowienie.Element(Ns + "WartoscZamowienia")!.Value);
        var rows = zamowienie.Elements(Ns + "ZamowienieWiersz").ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Sprzęt", rows[0].Element(Ns + "P_7Z")!.Value);
        Assert.Equal("1000.00", rows[0].Element(Ns + "P_11NettoZ")!.Value);
        Assert.Equal("230.00", rows[0].Element(Ns + "P_11VatZ")!.Value);
        Assert.Equal("23", rows[0].Element(Ns + "P_12Z")!.Value);
        Assert.Equal("Montaż", rows[1].Element(Ns + "P_7Z")!.Value);
        Assert.Equal("80.00", rows[1].Element(Ns + "P_11VatZ")!.Value);
    }

    [Fact]
    public void Generate_ThrowsWhenAdvanceMixesWithRowsOrCorrection()
    {
        var withRows = CreateAdvance();
        withRows.Lines.Add(new KsefInvoiceLine { Name = "Wiersz", Quantity = 1, UnitNetPrice = 10m, VatRate = 23 });
        Assert.Throws<ArgumentException>(() => CreateGenerator().Generate(withRows));

        var withCorrection = CreateAdvance();
        withCorrection.Correction = new KsefInvoiceCorrection
        {
            Reason = "Pomyłka",
            CorrectedNumber = "ZAL/2026/08/000",
            CorrectedIssueDate = new DateOnly(2026, 8, 1)
        };
        Assert.Throws<ArgumentException>(() => CreateGenerator().Generate(withCorrection));
    }

    [Fact]
    public void Generate_ThrowsWhenAdvanceHasNoPaymentOrNoOrder()
    {
        var noPayment = CreateAdvance();
        noPayment.Advance!.Payments.Clear();
        Assert.Throws<ArgumentException>(() => CreateGenerator().Generate(noPayment));

        var noOrder = CreateAdvance();
        noOrder.Advance!.OrderLines.Clear();
        Assert.Throws<ArgumentException>(() => CreateGenerator().Generate(noOrder));
    }
}
