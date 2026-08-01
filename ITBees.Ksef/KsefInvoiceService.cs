using System.Text;
using ITBees.Ksef.Auth;
using ITBees.Ksef.Configuration;
using ITBees.Ksef.Http;
using ITBees.Ksef.Invoicing;
using ITBees.Ksef.Models;
using ITBees.Ksef.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ITBees.Ksef;

public class KsefInvoiceService : IKsefInvoiceService
{
    private readonly IKsefApiClient _api;
    private readonly IKsefAuthenticationService _auth;
    private readonly IFa3XmlGenerator _xmlGenerator;
    private readonly KsefOptions _options;
    private readonly ILogger<KsefInvoiceService> _logger;

    public KsefInvoiceService(IKsefApiClient api, IKsefAuthenticationService auth, IFa3XmlGenerator xmlGenerator,
        IOptions<KsefOptions> options, ILogger<KsefInvoiceService> logger)
    {
        _api = api;
        _auth = auth;
        _xmlGenerator = xmlGenerator;
        _options = options.Value;
        _logger = logger;
    }

    public Task<KsefInvoiceSendResult> SendInvoiceAsync(KsefInvoice invoice, CancellationToken ct = default) =>
        SendInvoiceXmlAsync(_xmlGenerator.Generate(invoice), ct);

    public async Task<KsefInvoiceSendResult> SendInvoiceXmlAsync(string invoiceXml, CancellationToken ct = default)
    {
        var accessToken = await _auth.GetAccessTokenAsync(ct);

        var certificates = await _api.GetPublicKeyCertificatesAsync(ct);
        var encryptionCertificateInfo = KsefAuthenticationService.SelectCertificate(certificates,
            PublicKeyCertificateUsage.SymmetricKeyEncryption);
        using var encryptionCertificate = KsefCryptography.LoadCertificate(encryptionCertificateInfo.Certificate);

        var aesKey = KsefCryptography.GenerateAes256Key();
        var iv = KsefCryptography.GenerateIv();

        var openRequest = new OpenOnlineSessionRequest
        {
            FormCode = FormCode.Fa3,
            Encryption = new EncryptionInfo
            {
                EncryptedSymmetricKey =
                    Convert.ToBase64String(KsefCryptography.EncryptRsaOaepSha256(aesKey, encryptionCertificate)),
                InitializationVector = Convert.ToBase64String(iv),
                PublicKeyId = string.IsNullOrEmpty(encryptionCertificateInfo.PublicKeyId)
                    ? null
                    : encryptionCertificateInfo.PublicKeyId
            }
        };

        var session = await _api.OpenOnlineSessionAsync(openRequest, accessToken, ct);
        _logger.LogInformation("Opened KSeF online session {SessionReferenceNumber}.", session.ReferenceNumber);

        try
        {
            var plaintext = Encoding.UTF8.GetBytes(invoiceXml);
            var ciphertext = KsefCryptography.EncryptAes256Cbc(plaintext, aesKey, iv);

            var sendRequest = new SendInvoiceRequest
            {
                InvoiceHash = KsefCryptography.Sha256Base64(plaintext),
                InvoiceSize = plaintext.LongLength,
                EncryptedInvoiceHash = KsefCryptography.Sha256Base64(ciphertext),
                EncryptedInvoiceSize = ciphertext.LongLength,
                EncryptedInvoiceContent = Convert.ToBase64String(ciphertext)
            };

            var sent = await _api.SendInvoiceAsync(session.ReferenceNumber, sendRequest, accessToken, ct);
            var status = await WaitForInvoiceAcceptanceAsync(session.ReferenceNumber, sent.ReferenceNumber,
                accessToken, ct);

            var result = new KsefInvoiceSendResult
            {
                SessionReferenceNumber = session.ReferenceNumber,
                InvoiceReferenceNumber = sent.ReferenceNumber,
                KsefNumber = status.KsefNumber ?? string.Empty,
                AcquisitionDate = status.AcquisitionDate,
                InvoiceXml = invoiceXml
            };

            await CloseSessionQuietlyAsync(session.ReferenceNumber, accessToken);
            result.UpoXml = await TryDownloadUpoAsync(session.ReferenceNumber, result.KsefNumber, accessToken, ct);

            _logger.LogInformation("Invoice accepted by KSeF with number {KsefNumber}.", result.KsefNumber);
            return result;
        }
        catch
        {
            await CloseSessionQuietlyAsync(session.ReferenceNumber, accessToken);
            throw;
        }
    }

    private async Task<SessionInvoiceStatusResponse> WaitForInvoiceAcceptanceAsync(string sessionReferenceNumber,
        string invoiceReferenceNumber, string accessToken, CancellationToken ct)
    {
        SessionInvoiceStatusResponse? status = null;
        for (var attempt = 0; attempt < _options.StatusPollMaxAttempts; attempt++)
        {
            status = await _api.GetSessionInvoiceStatusAsync(sessionReferenceNumber, invoiceReferenceNumber,
                accessToken, ct);

            if (status.Status.Code == 200)
                return status;
            if (status.Status.Code >= 400)
                throw new KsefApiException(
                    $"Invoice rejected by KSeF (session {sessionReferenceNumber}, invoice {invoiceReferenceNumber}): {status.Status}",
                    ksefStatusCode: status.Status.Code);

            await Task.Delay(_options.StatusPollDelayMs, ct);
        }

        throw new KsefApiException(
            $"Invoice processing did not complete within {_options.StatusPollMaxAttempts} status checks " +
            $"(session {sessionReferenceNumber}, invoice {invoiceReferenceNumber}, last status: {status?.Status}).");
    }

    private async Task CloseSessionQuietlyAsync(string sessionReferenceNumber, string accessToken)
    {
        try
        {
            await _api.CloseOnlineSessionAsync(sessionReferenceNumber, accessToken, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Session expires server-side after 12h anyway; closing failure must not mask the invoice result.
            _logger.LogWarning(ex, "Failed to close KSeF session {SessionReferenceNumber}.", sessionReferenceNumber);
        }
    }

    private async Task<string?> TryDownloadUpoAsync(string sessionReferenceNumber, string ksefNumber,
        string accessToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ksefNumber))
            return null;

        for (var attempt = 0; attempt < _options.StatusPollMaxAttempts; attempt++)
        {
            try
            {
                return await _api.GetInvoiceUpoAsync(sessionReferenceNumber, ksefNumber, accessToken, ct);
            }
            catch (KsefApiException ex)
            {
                _logger.LogDebug(ex, "UPO for {KsefNumber} not ready yet (attempt {Attempt}).", ksefNumber,
                    attempt + 1);
                await Task.Delay(_options.StatusPollDelayMs, ct);
            }
        }

        _logger.LogWarning(
            "UPO for invoice {KsefNumber} was not available; it can be downloaded later via IKsefApiClient.GetInvoiceUpoAsync.",
            ksefNumber);
        return null;
    }
}
