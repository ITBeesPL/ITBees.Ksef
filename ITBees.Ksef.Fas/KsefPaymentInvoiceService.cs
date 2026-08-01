using ITBees.FAS.Payments.Interfaces.Models;
using ITBees.Interfaces.Platforms;
using ITBees.Ksef.Invoicing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ITBees.Ksef.Fas;

/// <summary>
/// Issues KSeF e-invoices for paid subscription payment sessions (checkout, subscription renewals,
/// browser-redirect confirmations — every path that closes a PaymentSession as successful).
/// PaymentSession.InvoiceCreated plus a unique index on KsefInvoiceRecord.PaymentSessionGuid guarantee
/// exactly one invoice per payment, no matter how many closing paths fire.
/// </summary>
/// <typeparam name="TContext">Host application DbContext with PaymentSession and KsefInvoiceRecord registered
/// (call <see cref="Setup.KsefFasDbModelBuilder.RegisterDbModels"/> in OnModelCreating).</typeparam>
public class KsefPaymentInvoiceService<TContext> : IKsefPaymentInvoiceService where TContext : DbContext
{
    private const int MaxAttempts = 10;
    private const int BatchSize = 20;

    private readonly TContext _context;
    private readonly IKsefInvoiceService _ksefInvoiceService;
    private readonly IPlatformSettingsService _platformSettingsService;
    private readonly ILogger<KsefPaymentInvoiceService<TContext>> _logger;

    public KsefPaymentInvoiceService(TContext context, IKsefInvoiceService ksefInvoiceService,
        IPlatformSettingsService platformSettingsService, ILogger<KsefPaymentInvoiceService<TContext>> logger)
    {
        _context = context;
        _ksefInvoiceService = ksefInvoiceService;
        _platformSettingsService = platformSettingsService;
        _logger = logger;
    }

    public async Task<int> IssuePendingInvoicesAsync(CancellationToken ct = default)
    {
        var sessions = await _context.Set<PaymentSession>()
            .Include(x => x.InvoiceData).ThenInclude(x => x.SubscriptionPlan)
            .Where(x => x.Finished && x.Success && !x.Refunded && !x.InvoiceCreated && x.InvoiceDataGuid != null)
            .OrderBy(x => x.FinishedDate ?? x.Created)
            .Take(BatchSize)
            .ToListAsync(ct);

        var sentCount = 0;
        foreach (var session in sessions)
        {
            ct.ThrowIfCancellationRequested();
            if (await ProcessSessionAsync(session, ct))
                sentCount++;
        }

        return sentCount;
    }

    private async Task<bool> ProcessSessionAsync(PaymentSession session, CancellationToken ct)
    {
        var record = await _context.Set<KsefInvoiceRecord>()
            .FirstOrDefaultAsync(x => x.PaymentSessionGuid == session.Guid, ct);

        // Crash-recovery: invoice already delivered to KSeF, only the session flag was not persisted.
        if (record is { Status: KsefInvoiceRecordStatus.Sent or KsefInvoiceRecordStatus.Skipped })
        {
            await MarkSessionInvoicedAsync(session, ct);
            return false;
        }

        if (record is { Status: KsefInvoiceRecordStatus.Failed })
            return false; // permanent failure — requires manual intervention, do not retry automatically

        var plan = session.InvoiceData?.SubscriptionPlan;
        if (session.InvoiceData == null || plan == null)
        {
            _logger.LogWarning("Payment session {Guid} has no InvoiceData/SubscriptionPlan — skipping KSeF invoice.",
                session.Guid);
            return false;
        }

        var paymentDate = session.FinishedDate ?? session.Created;

        if (record == null)
        {
            record = await CreateRecordWithNextNumberAsync(session.Guid, paymentDate, ct);

            // Free/trial plans produce no taxable sale — nothing to invoice.
            if (plan.NetValue <= 0m)
            {
                record.Status = KsefInvoiceRecordStatus.Skipped;
                await _context.SaveChangesAsync(ct);
                await MarkSessionInvoicedAsync(session, ct);
                return false;
            }
        }

        try
        {
            var invoice = BuildInvoice(record, session, paymentDate);
            var result = await _ksefInvoiceService.SendInvoiceAsync(invoice, ct);

            record.Status = KsefInvoiceRecordStatus.Sent;
            record.KsefNumber = result.KsefNumber;
            record.KsefSessionReferenceNumber = result.SessionReferenceNumber;
            record.InvoiceXml = result.InvoiceXml;
            record.UpoXml = result.UpoXml;
            record.SentDate = DateTime.UtcNow;
            record.LastError = null;
            await _context.SaveChangesAsync(ct);

            await MarkSessionInvoicedAsync(session, ct);

            _logger.LogInformation(
                "KSeF invoice {InvoiceNumber} sent for payment session {SessionGuid}, KSeF number {KsefNumber}.",
                record.InvoiceNumber, session.Guid, result.KsefNumber);
            return true;
        }
        catch (Exception e)
        {
            record.Attempts++;
            record.LastError = Truncate($"{e.GetType().Name}: {e.Message}");
            if (record.Attempts >= MaxAttempts)
            {
                record.Status = KsefInvoiceRecordStatus.Failed;
                _logger.LogError(e,
                    "KSeF invoice {InvoiceNumber} for session {SessionGuid} permanently failed after {Attempts} attempts.",
                    record.InvoiceNumber, session.Guid, record.Attempts);
            }
            else
            {
                _logger.LogWarning(e,
                    "KSeF invoice {InvoiceNumber} for session {SessionGuid} failed (attempt {Attempts}/{MaxAttempts}), will retry.",
                    record.InvoiceNumber, session.Guid, record.Attempts, MaxAttempts);
            }

            await _context.SaveChangesAsync(CancellationToken.None);
            return false;
        }
    }

    private KsefInvoice BuildInvoice(KsefInvoiceRecord record, PaymentSession session, DateTime paymentDate)
    {
        var invoiceData = session.InvoiceData;
        var plan = invoiceData.SubscriptionPlan;
        var platformName = _platformSettingsService.GetSetting("PlatformName");
        var issueDate = DateOnly.FromDateTime(paymentDate);

        return new KsefInvoice
        {
            Number = record.InvoiceNumber,
            IssueDate = issueDate,
            SaleDate = issueDate,
            Currency = string.IsNullOrWhiteSpace(plan.Currency) ? "PLN" : plan.Currency.ToUpperInvariant(),
            Buyer = new KsefParty
            {
                Nip = string.IsNullOrWhiteSpace(invoiceData.NIP) ? null : invoiceData.NIP.Trim(),
                Name = invoiceData.CompanyName,
                AddressLine1 = invoiceData.Street,
                AddressLine2 = $"{invoiceData.PostCode} {invoiceData.City}".Trim(),
                CountryCode = string.IsNullOrWhiteSpace(invoiceData.Country) ? "PL" : invoiceData.Country.Trim(),
                Email = string.IsNullOrWhiteSpace(invoiceData.InvoiceEmail) ? null : invoiceData.InvoiceEmail
            },
            Lines =
            {
                new KsefInvoiceLine
                {
                    Name = $"{platformName} - {plan.PlanName}",
                    Unit = "szt.",
                    Quantity = 1m,
                    UnitNetPrice = plan.NetValue,
                    NetValue = plan.NetValue,
                    VatRate = plan.VatPercentage
                }
            },
            IsPaid = true,
            PaymentDate = issueDate
        };
    }

    /// <summary>Allocates the next per-month sequence number; retries on unique index collision.</summary>
    private async Task<KsefInvoiceRecord> CreateRecordWithNextNumberAsync(Guid paymentSessionGuid,
        DateTime paymentDate, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var year = paymentDate.Year;
            var month = paymentDate.Month;
            var lastSequence = await _context.Set<KsefInvoiceRecord>()
                .Where(x => x.Year == year && x.Month == month)
                .MaxAsync(x => (int?)x.SequenceNumber, ct) ?? 0;

            var record = new KsefInvoiceRecord
            {
                PaymentSessionGuid = paymentSessionGuid,
                Year = year,
                Month = month,
                SequenceNumber = lastSequence + 1,
                InvoiceNumber = $"FV/{lastSequence + 1}/{month:D2}/{year}",
                Status = KsefInvoiceRecordStatus.Pending,
                Created = DateTime.UtcNow
            };

            _context.Add(record);
            try
            {
                await _context.SaveChangesAsync(ct);
                return record;
            }
            catch (DbUpdateException)
            {
                _context.Entry(record).State = EntityState.Detached;

                var existing = await _context.Set<KsefInvoiceRecord>()
                    .FirstOrDefaultAsync(x => x.PaymentSessionGuid == paymentSessionGuid, ct);
                if (existing != null)
                    return existing;
                // Otherwise another writer took this sequence number — retry with the next one.
            }
        }

        throw new InvalidOperationException(
            $"Could not allocate a KSeF invoice number for payment session {paymentSessionGuid}.");
    }

    private async Task MarkSessionInvoicedAsync(PaymentSession session, CancellationToken ct)
    {
        await _context.Set<PaymentSession>()
            .Where(x => x.Guid == session.Guid)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.InvoiceCreated, true), ct);
    }

    private static string Truncate(string value) => value.Length <= 2000 ? value : value[..2000];
}
