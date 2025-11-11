using ITBees.Ksef.Core;
using ITBees.Ksef.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class App
{
    private readonly ILogger<App> _log;
    private readonly IKsefClient _client;
    private readonly KsefOptions _opt;

    public App(ILogger<App> log, IKsefClient client, IOptions<KsefOptions> opt)
    {
        _log = log; _client = client; _opt = opt.Value;
    }

    public async Task RunAsync()
    {
        _log.LogInformation("=== KSeF 2.0 (TE) console ===");
        Console.Write("NIP: ");
        var nip = (Console.ReadLine() ?? "").Trim();

        Console.Write("Token autoryzacyjny (TE): ");
        var token = (Console.ReadLine() ?? "").Trim();

        if (nip.Length == 0 || token.Length == 0)
        {
            Console.WriteLine("Brak NIP albo tokenu.");
            return;
        }

        try
        {
            // 1) Otwórz sesję po tokenie
            var sessionId = await _client.OpenSessionByTokenAsync(nip, token, CancellationToken.None);
            _log.LogInformation("Session opened: {Session}", sessionId);

            // 2) (Placeholder) Wyślij minimalny „pusty” plik — tu wpięta będzie realna p7m
            var payload = System.Text.Encoding.UTF8.GetBytes("<Test/>"); // TODO: real encrypted CMS/PKCS#7
            var referenceId = await _client.SubmitInvoiceAsync(sessionId, payload, CancellationToken.None);
            _log.LogInformation("Submitted. Ref: {Ref}", referenceId);

            // 3) Sprawdź status
            var status = await _client.GetStatusAsync(sessionId, referenceId, CancellationToken.None);
            _log.LogInformation("Status: {Status}; KSeF: {KsefNumber}; Reason: {Reason}",
                status.Status, status.KsefNumber ?? "-", status.RejectionReason ?? "-");

            // 4) Jeżeli Accepted → pobierz UPO
            if (string.Equals(status.Status, "Accepted", StringComparison.OrdinalIgnoreCase))
            {
                var upo = await _client.DownloadUpoAsync(sessionId, referenceId, CancellationToken.None);
                var path = Path.Combine(AppContext.BaseDirectory, $"UPO_{referenceId}.pdf");
                await File.WriteAllBytesAsync(path, upo);
                _log.LogInformation("UPO saved to {Path}", path);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Błąd komunikacji z KSeF TE");
        }
    }
}