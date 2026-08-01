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
