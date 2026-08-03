using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ITBees.Interfaces.Repository;
using ITBees.Ksef.Configuration;
using ITBees.Ksef.Credentials.Api;
using ITBees.Ksef.Credentials.Security;
using ITBees.Ksef.DependencyInjection;
using ITBees.Ksef.Models;
using ITBees.RestfulApiControllers.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ITBees.Ksef.Credentials;

public interface IKsefCredentialService
{
    /// <summary>Integration state without secrets — what the UI is allowed to show.</summary>
    KsefCredentialVm GetStatus();

    KsefCredentialVm Save(KsefCredentialIm im);

    void Delete();

    /// <summary>Checks whether the stored credential can actually log in to KSeF.</summary>
    Task<KsefConnectionTestVm> TestConnectionAsync(CancellationToken ct = default);

    /// <summary>
    /// Builds <see cref="KsefOptions"/> from the current company's credential.
    /// Throws when the company has no integration configured yet.
    /// </summary>
    KsefOptions ResolveOptions();
}

public class KsefCredentialService : IKsefCredentialService
{
    private readonly IReadOnlyRepository<KsefCredential> _credentialRoRepository;
    private readonly IWriteOnlyRepository<KsefCredential> _credentialWoRepository;
    private readonly IKsefCompanyContext _companyContext;
    private readonly ISecretProtector _secrets;
    private readonly IKsefClientFactory _ksefClientFactory;
    private readonly IKsefCredentialAuditSink _audit;
    private readonly KsefCredentialsOptions _options;
    private readonly ILogger<KsefCredentialService> _logger;

    public KsefCredentialService(IReadOnlyRepository<KsefCredential> credentialRoRepository,
        IWriteOnlyRepository<KsefCredential> credentialWoRepository, IKsefCompanyContext companyContext,
        ISecretProtector secrets, IKsefClientFactory ksefClientFactory, IKsefCredentialAuditSink audit,
        IOptions<KsefCredentialsOptions> options, ILogger<KsefCredentialService> logger)
    {
        _credentialRoRepository = credentialRoRepository;
        _credentialWoRepository = credentialWoRepository;
        _companyContext = companyContext;
        _secrets = secrets;
        _ksefClientFactory = ksefClientFactory;
        _audit = audit;
        _options = options.Value;
        _logger = logger;
    }

    public KsefCredentialVm GetStatus()
    {
        var companyGuid = _companyContext.GetCurrentCompanyGuid();
        var credential = _credentialRoRepository.GetFirst(x => x.CompanyGuid == companyGuid);

        if (credential == null)
            return new KsefCredentialVm { Configured = false };

        return new KsefCredentialVm
        {
            Configured = true,
            Kind = credential.Kind,
            Environment = credential.Environment,
            Nip = credential.Nip,
            MaskedToken = credential.Kind == KsefCredentialKind.Token ? MaskToken(credential) : null,
            CertificateFileName = credential.CertificateFileName,
            CertificateSubject = credential.CertificateSubject,
            CertificateThumbprint = credential.CertificateThumbprint,
            CertificateValidFrom = credential.CertificateValidFrom,
            CertificateValidTo = credential.CertificateValidTo,
            CertificateExpired = credential.IsCertificateExpired(DateTime.UtcNow),
            LastVerifiedAt = credential.LastVerifiedAt,
            LastError = credential.LastError,
            Created = credential.Created,
            Updated = credential.Updated
        };
    }

    public KsefCredentialVm Save(KsefCredentialIm im)
    {
        var companyGuid = _companyContext.GetCurrentCompanyGuid();
        var existing = _credentialRoRepository.GetFirst(x => x.CompanyGuid == companyGuid);

        var nip = NormalizeNip(im.Nip) ?? NormalizeNip(_companyContext.GetCurrentCompanyNip())
            ?? throw new ArgumentException(
                "Do integracji z KSeF potrzebny jest NIP — uzupełnij dane firmy albo podaj NIP kontekstu.");

        if (existing == null)
        {
            var created = _credentialWoRepository.InsertData(BuildCredential(new KsefCredential
            {
                Guid = Guid.NewGuid(),
                CompanyGuid = companyGuid,
                Created = DateTime.UtcNow
            }, im, nip));

            _audit.Created(companyGuid, KsefCredentialAuditView.From(created));
            return GetStatus();
        }

        // The previous KSeF session is stale even when only the context NIP changed.
        InvalidateCachedSession(existing);

        var before = KsefCredentialAuditView.From(existing);
        var updated = _credentialWoRepository
            .UpdateData(x => x.Guid == existing.Guid, x => BuildCredential(x, im, nip))
            .First();

        _audit.Updated(companyGuid, before, KsefCredentialAuditView.From(updated));
        return GetStatus();
    }

    public void Delete()
    {
        var companyGuid = _companyContext.GetCurrentCompanyGuid();
        var credential = _credentialRoRepository.GetFirst(x => x.CompanyGuid == companyGuid);
        if (credential == null)
            return;

        InvalidateCachedSession(credential);
        _credentialWoRepository.DeleteData(x => x.Guid == credential.Guid);
        _audit.Deleted(companyGuid, KsefCredentialAuditView.From(credential));
    }

    public async Task<KsefConnectionTestVm> TestConnectionAsync(CancellationToken ct = default)
    {
        var companyGuid = _companyContext.GetCurrentCompanyGuid();
        var credential = _credentialRoRepository.GetFirst(x => x.CompanyGuid == companyGuid)
                         ?? throw new ResultNotFoundException("Integracja z KSeF nie jest jeszcze skonfigurowana.");

        var options = BuildOptions(credential);
        var result = new KsefConnectionTestVm { CheckedAt = DateTime.UtcNow };

        try
        {
            // Cheapest full check: a metadata query over a narrow time window forces authentication
            // and changes nothing on the KSeF side.
            var query = _ksefClientFactory.CreateQueryService(options);
            await query.QueryAsync(new KsefInvoiceQueryFilter
            {
                From = DateTimeOffset.UtcNow.AddDays(-1),
                To = DateTimeOffset.UtcNow,
                SubjectType = InvoiceQuerySubjectType.Subject2,
                PageSize = 1,
                MaxInvoices = 1
            }, ct);

            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "KSeF connection test failed for company {CompanyGuid}.", companyGuid);
            result.Success = false;
            result.Error = ex.Message;
        }

        _credentialWoRepository.UpdateData(x => x.Guid == credential.Guid, x =>
        {
            x.LastVerifiedAt = result.Success ? result.CheckedAt : x.LastVerifiedAt;
            x.LastError = result.Error;
        });

        _audit.ConnectionTested(companyGuid, KsefCredentialAuditView.From(credential), result.Success, result.Error);

        return result;
    }

    public KsefOptions ResolveOptions()
    {
        var companyGuid = _companyContext.GetCurrentCompanyGuid();
        var credential = _credentialRoRepository.GetFirst(x => x.CompanyGuid == companyGuid)
                         ?? throw new ResultNotFoundException(
                             "Integracja z KSeF nie jest skonfigurowana — dodaj token lub certyfikat w ustawieniach.");

        return BuildOptions(credential);
    }

    private KsefCredential BuildCredential(KsefCredential credential, KsefCredentialIm im, string nip)
    {
        credential.Kind = im.Kind;
        credential.Environment = im.Environment;
        credential.Nip = nip;
        credential.VerifyCertificateChain = im.VerifyCertificateChain;
        credential.Updated = DateTime.UtcNow;
        credential.LastError = null;
        credential.LastVerifiedAt = null;

        if (im.Kind == KsefCredentialKind.Certificate)
            ApplyCertificate(credential, im);
        else
            ApplyToken(credential, im);

        return credential;
    }

    private KsefOptions BuildOptions(KsefCredential credential)
    {
        var options = new KsefOptions
        {
            Environment = credential.Environment,
            Nip = credential.Nip,
            SystemInfo = _options.SystemInfo
        };

        if (credential.Kind == KsefCredentialKind.Certificate)
        {
            if (string.IsNullOrWhiteSpace(credential.EncryptedCertificate))
                throw new ResultNotFoundException("Zapisany certyfikat KSeF jest pusty — wgraj go ponownie.");

            if (credential.IsCertificateExpired(DateTime.UtcNow))
                throw new ArgumentException(
                    $"Certyfikat KSeF stracił ważność {credential.CertificateValidTo:d} — wgraj nowy.");

            options.AuthMode = KsefAuthMode.Certificate;
            options.Certificate = new KsefCertificateOptions
            {
                Pkcs12Base64 = _secrets.Unprotect(credential.EncryptedCertificate),
                Password = string.IsNullOrEmpty(credential.EncryptedCertificatePassword)
                    ? null
                    : _secrets.Unprotect(credential.EncryptedCertificatePassword),
                VerifyCertificateChain = credential.VerifyCertificateChain
            };
        }
        else
        {
            if (string.IsNullOrWhiteSpace(credential.EncryptedToken))
                throw new ResultNotFoundException("Zapisany token KSeF jest pusty — wprowadź go ponownie.");

            options.AuthMode = KsefAuthMode.Token;
            options.KsefToken = _secrets.Unprotect(credential.EncryptedToken);
        }

        return options;
    }

    private void ApplyToken(KsefCredential credential, KsefCredentialIm im)
    {
        if (string.IsNullOrWhiteSpace(im.Token))
            throw new ArgumentException("Token KSeF jest wymagany.");

        credential.EncryptedToken = _secrets.Protect(im.Token.Trim());
        credential.EncryptedCertificate = null;
        credential.EncryptedCertificatePassword = null;
        credential.CertificateFileName = null;
        credential.CertificateSubject = null;
        credential.CertificateThumbprint = null;
        credential.CertificateValidFrom = null;
        credential.CertificateValidTo = null;
    }

    private void ApplyCertificate(KsefCredential credential, KsefCredentialIm im)
    {
        if (string.IsNullOrWhiteSpace(im.CertificateBase64))
            throw new ArgumentException("Plik certyfikatu jest wymagany.");

        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(im.CertificateBase64);
        }
        catch (FormatException)
        {
            throw new ArgumentException("Certyfikat nie jest poprawnie zakodowany w Base64.");
        }

        // Loading it here instead of on the first invoice send turns "KSeF rejected the invoice"
        // into a readable message while the user is still on the settings screen.
        using var certificate = LoadCertificate(raw, im.CertificatePassword);

        if (!certificate.HasPrivateKey)
            throw new ArgumentException(
                "Wgrany certyfikat nie zawiera klucza prywatnego — wyeksportuj go jako .p12/.pfx razem z kluczem.");

        if (certificate.NotAfter.ToUniversalTime() < DateTime.UtcNow)
            throw new ArgumentException($"Certyfikat stracił ważność {certificate.NotAfter:d}.");

        credential.EncryptedCertificate = _secrets.Protect(Convert.ToBase64String(raw));
        credential.EncryptedCertificatePassword = string.IsNullOrEmpty(im.CertificatePassword)
            ? null
            : _secrets.Protect(im.CertificatePassword);
        credential.CertificateFileName = im.CertificateFileName;
        credential.CertificateSubject = certificate.Subject;
        credential.CertificateThumbprint = certificate.Thumbprint;
        credential.CertificateValidFrom = certificate.NotBefore.ToUniversalTime();
        credential.CertificateValidTo = certificate.NotAfter.ToUniversalTime();
        credential.EncryptedToken = null;
    }

    private static X509Certificate2 LoadCertificate(byte[] raw, string? password)
    {
        try
        {
            return new X509Certificate2(raw, password, X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException ex)
        {
            throw new ArgumentException(
                "Nie udało się odczytać certyfikatu — sprawdź hasło i format pliku (.p12/.pfx). " + ex.Message);
        }
    }

    /// <summary>Drops the cached session so an old credential stops working once it is replaced.</summary>
    private void InvalidateCachedSession(KsefCredential credential)
    {
        try
        {
            _ksefClientFactory.InvalidateAuthentication(BuildOptions(credential));
        }
        catch (Exception ex)
        {
            // The previous credential may already have been incomplete — no reason to block saving a new one.
            _logger.LogDebug(ex, "Could not invalidate the KSeF session of company {CompanyGuid}.",
                credential.CompanyGuid);
        }
    }

    private string? MaskToken(KsefCredential credential)
    {
        if (string.IsNullOrEmpty(credential.EncryptedToken))
            return null;

        try
        {
            var token = _secrets.Unprotect(credential.EncryptedToken);
            return token.Length <= 12 ? new string('•', token.Length) : $"{token[..8]}…{token[^4..]}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not decrypt the KSeF token of company {CompanyGuid}.",
                credential.CompanyGuid);
            return null;
        }
    }

    private static string? NormalizeNip(string? nip)
    {
        if (string.IsNullOrWhiteSpace(nip))
            return null;

        var digits = new string(nip.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }
}
