using ITBees.Ksef;
using ITBees.Ksef.Configuration;
using ITBees.Ksef.Invoicing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TestKsefConsoleApp;

/// <summary>
/// Interactive smoke test against the KSeF TEST environment:
/// generates a sample FA(3) invoice, sends it through an online session and stores the UPO.
/// </summary>
public sealed class App
{
    private readonly ILogger<App> _log;
    private readonly IKsefInvoiceService _invoiceService;
    private readonly IFa3XmlGenerator _xmlGenerator;
    private readonly KsefOptions _options;

    public App(ILogger<App> log, IKsefInvoiceService invoiceService, IFa3XmlGenerator xmlGenerator,
        IOptions<KsefOptions> options)
    {
        _log = log;
        _invoiceService = invoiceService;
        _xmlGenerator = xmlGenerator;
        _options = options.Value;
    }

    public async Task RunAsync()
    {
        if (string.IsNullOrWhiteSpace(_options.KsefToken) || string.IsNullOrWhiteSpace(_options.Nip))
        {
            _log.LogError(
                "Fill in Ksef:KsefToken and Ksef:Nip in appsettings.json (token generated on https://ksef-test.mf.gov.pl for your test NIP).");
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var invoice = new KsefInvoice
        {
            Number = $"TEST/{DateTime.UtcNow:yyyyMMddHHmmss}",
            IssueDate = today,
            SaleDate = today,
            Currency = "PLN",
            Buyer = new KsefParty
            {
                Nip = "1111111111",
                Name = "F.H.U. Jan Kowalski",
                AddressLine1 = "ul. Polna 1",
                AddressLine2 = "00-001 Warszawa"
            },
            Lines =
            {
                new KsefInvoiceLine
                {
                    Name = "Abonament testowy",
                    Unit = "szt.",
                    Quantity = 1,
                    UnitNetPrice = 100.00m,
                    VatRate = 23
                }
            },
            IsPaid = true,
            PaymentDate = today
        };

        var xml = _xmlGenerator.Generate(invoice);
        _log.LogInformation("Generated FA(3) XML:\n{Xml}", xml);

        var result = await _invoiceService.SendInvoiceXmlAsync(xml);
        _log.LogInformation("KSeF number: {KsefNumber} (session {Session}, invoice ref {InvoiceRef})",
            result.KsefNumber, result.SessionReferenceNumber, result.InvoiceReferenceNumber);

        if (result.UpoXml != null)
        {
            var upoPath = Path.Combine(AppContext.BaseDirectory, $"UPO_{result.KsefNumber}.xml");
            await File.WriteAllTextAsync(upoPath, result.UpoXml);
            _log.LogInformation("UPO saved to {Path}", upoPath);
        }
    }
}
