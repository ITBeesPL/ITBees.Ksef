using ITBees.Ksef.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ITBees.Ksef.Fas;

/// <summary>
/// Background outbox processor: periodically issues KSeF e-invoices for successfully paid
/// payment sessions that do not have an invoice yet. Covers every payment-closing path
/// (payment operator webhook, browser-redirect confirmation, subscription renewals) and retries failures.
/// Inactive when Ksef:KsefToken is not configured.
/// </summary>
public class KsefInvoiceWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<KsefOptions> _ksefOptions;
    private readonly ILogger<KsefInvoiceWorker> _logger;

    public KsefInvoiceWorker(IServiceScopeFactory scopeFactory, IOptions<KsefOptions> ksefOptions,
        ILogger<KsefInvoiceWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _ksefOptions = ksefOptions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_ksefOptions.Value.KsefToken))
        {
            _logger.LogInformation("KSeF invoicing disabled — Ksef:KsefToken is not configured.");
            return;
        }

        _logger.LogInformation("KSeF invoice worker started (environment: {Environment}).",
            _ksefOptions.Value.Environment);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IKsefPaymentInvoiceService>();
                var sent = await service.IssuePendingInvoicesAsync(stoppingToken);
                if (sent > 0)
                    _logger.LogInformation("KSeF invoice worker sent {Count} invoice(s).", sent);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "KSeF invoice worker iteration failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
