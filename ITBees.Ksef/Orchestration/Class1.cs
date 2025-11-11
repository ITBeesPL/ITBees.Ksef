using ITBees.Ksef.Core;
using ITBees.Ksef.Transport;
using Microsoft.Extensions.Logging;


namespace ITBees.Ksef.Orchestration;

public interface IInvoiceSender
{
    Task QueueAsync(IInvoiceDocument doc, CancellationToken ct);
    Task ProcessPendingAsync(int batchSize, CancellationToken ct); // wywołasz z własnego crona
}

public sealed class InvoiceSender : IInvoiceSender
{
    private readonly IInvoiceRepository _repo;
    private readonly IInvoicePrevalidator _validator;
    private readonly ICredentialStore _creds;
    private readonly IKsefEncryptionService _enc;
    private readonly IKsefClient _client;
    private readonly ILogger<InvoiceSender> _log;

    public InvoiceSender(IInvoiceRepository repo, IInvoicePrevalidator validator, ICredentialStore creds,
                         IKsefEncryptionService enc, IKsefClient client, ILogger<InvoiceSender> log)
    { _repo = repo; _validator = validator; _creds = creds; _enc = enc; _client = client; _log = log; }

    public async Task QueueAsync(IInvoiceDocument doc, CancellationToken ct)
    {
        var v = await _validator.ValidateAsync(doc.XmlRaw, ct);
        if (!v.IsValid) throw new InvalidOperationException("Invoice XSD/business validation failed: " + string.Join("; ", v.Errors));
        await _repo.SaveDraftAsync(doc, ct);
        await _repo.MarkQueuedAsync(doc.Id, ct);
    }

    public async Task ProcessPendingAsync(int batchSize, CancellationToken ct)
    {
        var pending = await _repo.GetPendingAsync(batchSize, ct);
        foreach (var doc in pending)
        {
            try
            {
                var creds = await _creds.GetAsync(doc.Issuer, ct);
                var session = await _client.OpenSessionAsync(doc.Issuer, creds.Token, ct);

                // Encrypt required by KSeF 2.0
                var payload = await _enc.EncryptAsync(System.Text.Encoding.UTF8.GetBytes(doc.XmlRaw), ct);
                var submit = await _client.SubmitInvoiceAsync(session, payload, ct);

                // Poll status (backoff: 5s, 15s, 60s) — tu minimalny inline backoff
                var delays = new[] { 5, 15, 60 };
                StatusResult status = default!;
                foreach (var s in delays)
                {
                    await Task.Delay(TimeSpan.FromSeconds(s), ct);
                    status = await _client.GetStatusAsync(session, submit.ReferenceId, ct);
                    if (status.Status is "Accepted" or "Rejected") break;
                }

                if (status.Status == "Accepted")
                {
                    await _repo.UpdateStateAsync(doc.Id, InvoiceState.Accepted, status.KsefNumber, null, ct);
                    if (status.UpoAvailableAt is not null)
                    {
                        var upo = await _client.DownloadUpoAsync(session, submit.ReferenceId, ct);
                        // TODO: persist UPO bytes in your storage
                    }
                }
                else if (status.Status == "Rejected")
                {
                    await _repo.UpdateStateAsync(doc.Id, InvoiceState.Rejected, null, status.RejectionReason, ct);
                }
                else
                {
                    // still processing at KSeF — zostaw do ponownej próby
                    _log.LogWarning("KSeF still processing. Will retry later. Ref={Ref}", submit.ReferenceId);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Send failed. Will retry later. InvoiceId={Id}", doc.Id);
                await _repo.UpdateStateAsync(doc.Id, InvoiceState.Failed, null, ex.Message, ct);
                // nie usuwaj z kolejki — Twój cron uruchomi ProcessPendingAsync ponownie
            }
        }
    }
}