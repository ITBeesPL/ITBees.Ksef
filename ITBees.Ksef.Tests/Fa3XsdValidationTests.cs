using System.Xml;
using System.Xml.Schema;
using ITBees.Ksef.Configuration;
using ITBees.Ksef.Invoicing;
using Microsoft.Extensions.Options;
using Xunit;

namespace ITBees.Ksef.Tests;

/// <summary>
/// Validates generator output against the official FA(3) XSD published by the Ministry of Finance
/// (schemat_FA(3)_v1-0E.xsd, namespace http://crd.gov.pl/wzor/2025/06/25/13775/).
/// </summary>
public class Fa3XsdValidationTests
{
    private static XmlSchemaSet LoadFa3SchemaSet()
    {
        var schemasDirectory = Path.Combine(AppContext.BaseDirectory, "Schemas");
        var schemaSet = new XmlSchemaSet
        {
            // The main schema references StrukturyDanych by an http URL; resolve locally instead.
            XmlResolver = new LocalSchemaResolver(schemasDirectory)
        };
        using (var reader = XmlReader.Create(Path.Combine(schemasDirectory, "schemat_FA(3)_v1-0E.xsd")))
        {
            schemaSet.Add(null, reader);
        }

        schemaSet.Compile();
        return schemaSet;
    }

    private static List<string> Validate(string xml)
    {
        var errors = new List<string>();
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema };
        settings.Schemas.Add(LoadFa3SchemaSet());
        settings.ValidationEventHandler += (_, e) => errors.Add(e.Message);
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        while (reader.Read())
        {
        }

        return errors;
    }

    private static Fa3XmlGenerator CreateGenerator() =>
        new(Options.Create(new KsefOptions
        {
            SystemInfo = "UnitTests",
            Seller = new KsefSellerOptions
            {
                Nip = "5555555555",
                Name = "Sprzedawca Sp. z o.o.",
                AddressLine1 = "ul. Testowa 1",
                AddressLine2 = "00-001 Warszawa",
                Email = "seller@example.com"
            }
        }));

    [Fact]
    public void GeneratedInvoice_IsValidAgainstOfficialFa3Xsd()
    {
        var invoice = new KsefInvoice
        {
            Number = "FV/2026/08/001",
            IssueDate = new DateOnly(2026, 8, 1),
            Currency = "PLN",
            Buyer = new KsefParty
            {
                Nip = "1111111111",
                Name = "Nabywca S.A.",
                AddressLine1 = "ul. Polna 2",
                AddressLine2 = "11-111 Kraków",
                Email = "buyer@example.com"
            },
            Lines =
            {
                new KsefInvoiceLine { Name = "Abonament roczny", Quantity = 1, UnitNetPrice = 813.01m, VatRate = 23 },
                new KsefInvoiceLine { Name = "Usługa dodatkowa", Quantity = 2, UnitNetPrice = 40.50m, VatRate = 8 }
            },
            IsPaid = true,
            PaymentDate = new DateOnly(2026, 8, 1)
        };

        var errors = Validate(CreateGenerator().Generate(invoice));

        Assert.True(errors.Count == 0, "XSD validation errors: " + string.Join(" | ", errors));
    }

    [Fact]
    public void GeneratedUnpaidInvoiceWithDueDateAndBankAccount_IsValidAgainstOfficialFa3Xsd()
    {
        var invoice = new KsefInvoice
        {
            Number = "FV/2026/08/005",
            IssueDate = new DateOnly(2026, 8, 25),
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
                new KsefInvoiceLine { Name = "Przegląd okresowy", Quantity = 1, UnitNetPrice = 10010m, VatRate = 23 }
            },
            PaymentDueDate = new DateOnly(2026, 9, 1),
            BankAccount = new KsefBankAccount
            {
                Number = "44114020040000340285638379",
                Description = "Rachunek firmowy"
            }
        };

        var errors = Validate(CreateGenerator().Generate(invoice));

        Assert.True(errors.Count == 0, "XSD validation errors: " + string.Join(" | ", errors));
    }

    [Fact]
    public void GeneratedPaidInvoiceWithDueDateAndBankAccount_IsValidAgainstOfficialFa3Xsd()
    {
        var invoice = new KsefInvoice
        {
            Number = "FV/2026/08/006",
            IssueDate = new DateOnly(2026, 8, 25),
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
                new KsefInvoiceLine { Name = "Usługa serwisowa", Quantity = 1, UnitNetPrice = 500m, VatRate = 23 }
            },
            IsPaid = true,
            PaymentDate = new DateOnly(2026, 8, 26),
            PaymentDueDate = new DateOnly(2026, 9, 1),
            BankAccount = new KsefBankAccount { Number = "44114020040000340285638379" }
        };

        var errors = Validate(CreateGenerator().Generate(invoice));

        Assert.True(errors.Count == 0, "XSD validation errors: " + string.Join(" | ", errors));
    }

    [Fact]
    public void GeneratedConsumerInvoice_IsValidAgainstOfficialFa3Xsd()
    {
        var invoice = new KsefInvoice
        {
            Number = "FV/2026/08/002",
            IssueDate = new DateOnly(2026, 8, 1),
            Currency = "PLN",
            Buyer = new KsefParty
            {
                Name = "Jan Kowalski",
                AddressLine1 = "ul. Polna 3",
                AddressLine2 = "22-222 Gdańsk"
            },
            Lines =
            {
                new KsefInvoiceLine { Name = "Abonament miesięczny", Quantity = 1, UnitNetPrice = 81.30m, VatRate = 23 }
            }
        };

        var errors = Validate(CreateGenerator().Generate(invoice));

        Assert.True(errors.Count == 0, "XSD validation errors: " + string.Join(" | ", errors));
    }

    [Fact]
    public void GeneratedCorrection_IsValidAgainstOfficialFa3Xsd()
    {
        var errors = Validate(CreateGenerator().Generate(CreateCorrection()));

        Assert.True(errors.Count == 0, "XSD validation errors: " + string.Join(" | ", errors));
    }

    [Fact]
    public void GeneratedCorrectionOfInvoiceIssuedOutsideKsef_IsValidAgainstOfficialFa3Xsd()
    {
        var invoice = CreateCorrection();
        invoice.Correction!.CorrectedKsefNumber = null;

        var errors = Validate(CreateGenerator().Generate(invoice));

        Assert.True(errors.Count == 0, "XSD validation errors: " + string.Join(" | ", errors));
    }

    [Fact]
    public void GeneratedInvoiceWithNotes_IsValidAgainstOfficialFa3Xsd()
    {
        var invoice = new KsefInvoice
        {
            Number = "FV/2026/08/003",
            IssueDate = new DateOnly(2026, 8, 1),
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
                new KsefInvoiceLine { Name = "Abonament roczny", Quantity = 1, UnitNetPrice = 813.01m, VatRate = 23 }
            },
            Notes = "Zamówienie nr 44/2026 — płatność w dwóch ratach.\nTowar odebrano osobiście."
        };

        var errors = Validate(CreateGenerator().Generate(invoice));

        Assert.True(errors.Count == 0, "XSD validation errors: " + string.Join(" | ", errors));
    }

    [Fact]
    public void GeneratedAdvanceInvoice_IsValidAgainstOfficialFa3Xsd()
    {
        var invoice = new KsefInvoice
        {
            Number = "ZAL/2026/08/001",
            IssueDate = new DateOnly(2026, 8, 20),
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
                    new KsefInvoiceLine
                    {
                        Name = "Sprzęt", Unit = "szt.", Quantity = 1, UnitNetPrice = 1000m, VatRate = 23
                    },
                    new KsefInvoiceLine
                    {
                        Name = "Montaż", Unit = "usł.", Quantity = 1, UnitNetPrice = 1000m, VatRate = 8
                    }
                }
            },
            IsPaid = true,
            PaymentDate = new DateOnly(2026, 8, 18),
            Notes = "Zaliczka na poczet zamówienia nr 44/2026."
        };

        var errors = Validate(CreateGenerator().Generate(invoice));

        Assert.True(errors.Count == 0, "XSD validation errors: " + string.Join(" | ", errors));
    }

    /// <summary>A correction down to zero: negative aggregates and rows carrying only the state before.</summary>
    [Fact]
    public void GeneratedFullReversal_IsValidAgainstOfficialFa3Xsd()
    {
        var invoice = CreateCorrection();
        invoice.Lines.RemoveAll(l => !l.StateBeforeCorrection);

        var errors = Validate(CreateGenerator().Generate(invoice));

        Assert.True(errors.Count == 0, "XSD validation errors: " + string.Join(" | ", errors));
    }

    private static KsefInvoice CreateCorrection() => new()
    {
        Number = "KOR/2026/08/001",
        IssueDate = new DateOnly(2026, 8, 20),
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
            Reason = "Zwrot części usług",
            Type = KsefCorrectionType.CorrectionPeriod,
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
            new KsefInvoiceLine
            {
                Name = "Licencja", Unit = "szt.", Quantity = 1, UnitNetPrice = 120m, VatRate = 8,
                StateBeforeCorrection = true
            },
            new KsefInvoiceLine { Name = "Konsultacje IT", Unit = "h", Quantity = 8, UnitNetPrice = 250m, VatRate = 23 },
            new KsefInvoiceLine { Name = "Licencja", Unit = "szt.", Quantity = 1, UnitNetPrice = 120m, VatRate = 8 }
        }
    };

    /// <summary>Redirects the http schemaLocation of StrukturyDanych/ElementarneTypyDanych/KodyKrajow to local files.</summary>
    private sealed class LocalSchemaResolver : XmlUrlResolver
    {
        private readonly string _schemasDirectory;

        public LocalSchemaResolver(string schemasDirectory) => _schemasDirectory = schemasDirectory;

        public override Uri ResolveUri(Uri? baseUri, string? relativeUri)
        {
            if (relativeUri != null)
            {
                var fileName = relativeUri.Split('/').Last();
                var localPath = Path.Combine(_schemasDirectory, fileName);
                if (File.Exists(localPath))
                    return new Uri(localPath);
            }

            return base.ResolveUri(baseUri, relativeUri);
        }
    }
}
