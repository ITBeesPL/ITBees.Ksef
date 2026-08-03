using ITBees.Ksef.Auth;
using ITBees.Ksef.Http;
using ITBees.Ksef.Models;
using Microsoft.Extensions.Logging;

namespace ITBees.Ksef;

public class KsefInvoiceQueryService : IKsefInvoiceQueryService
{
    private readonly IKsefApiClient _api;
    private readonly IKsefAuthenticationService _auth;
    private readonly ILogger<KsefInvoiceQueryService> _logger;

    public KsefInvoiceQueryService(IKsefApiClient api, IKsefAuthenticationService auth,
        ILogger<KsefInvoiceQueryService> logger)
    {
        _api = api;
        _auth = auth;
        _logger = logger;
    }

    public async Task<IReadOnlyList<KsefInvoiceMetadata>> QueryAsync(KsefInvoiceQueryFilter filter,
        CancellationToken ct = default)
    {
        if (filter.To < filter.From)
            throw new ArgumentException("KsefInvoiceQueryFilter.To must not be earlier than From.", nameof(filter));

        var accessToken = await _auth.GetAccessTokenAsync(ct);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var request = new InvoiceMetadataQueryRequest
        {
            SubjectType = filter.SubjectType,
            DateRange = new InvoiceQueryDateRange
            {
                DateType = filter.DateType,
                From = filter.From,
                To = filter.To
            }
        };

        var results = new List<KsefInvoiceMetadata>();
        for (var pageOffset = 0; results.Count < filter.MaxInvoices; pageOffset++)
        {
            var page = await _api.QueryInvoiceMetadataAsync(request, pageOffset, pageSize, accessToken, ct);
            if (page.Invoices.Count == 0)
                break;

            results.AddRange(page.Invoices);

            if (!page.HasMore)
                break;
        }

        if (results.Count > filter.MaxInvoices)
        {
            _logger.LogWarning(
                "KSeF returned more than the configured MaxInvoices ({MaxInvoices}) for {From:d}–{To:d}; truncating.",
                filter.MaxInvoices, filter.From, filter.To);
            results = results.Take(filter.MaxInvoices).ToList();
        }

        _logger.LogInformation("Fetched {Count} invoice(s) from KSeF for {SubjectType} between {From:d} and {To:d}.",
            results.Count, filter.SubjectType, filter.From, filter.To);

        return results;
    }

    public async Task<string> DownloadInvoiceXmlAsync(string ksefNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ksefNumber))
            throw new ArgumentException("KSeF number is required.", nameof(ksefNumber));

        var accessToken = await _auth.GetAccessTokenAsync(ct);
        return await _api.GetInvoiceXmlAsync(ksefNumber, accessToken, ct);
    }
}
