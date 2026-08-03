using System.Reflection;
using ITBees.Ksef.Configuration;
using ITBees.Ksef.Credentials;
using Xunit;

namespace ITBees.Ksef.Tests;

public class KsefCredentialAuditViewTests
{
    private const string Token = "v1:cGxhaW50ZXh0LXRva2VuLXNlY3JldA==";
    private const string Certificate = "v1:cGxhaW50ZXh0LWNlcnRpZmljYXRlLXNlY3JldA==";
    private const string CertificatePassword = "v1:cGxhaW50ZXh0LXBhc3N3b3Jk";

    /// <summary>
    /// The view is what hosts hand to their audit trail. A property added to the entity must not be
    /// able to drag a secret along with it, so this walks every property value instead of naming fields.
    /// </summary>
    [Fact]
    public void View_never_carries_a_secret()
    {
        var view = KsefCredentialAuditView.From(BuildCredential());

        var values = typeof(KsefCredentialAuditView)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.GetValue(view)?.ToString())
            .Append(view.ToString())
            .Where(value => !string.IsNullOrEmpty(value))
            .ToList();

        Assert.DoesNotContain(Token, values);
        Assert.DoesNotContain(Certificate, values);
        Assert.DoesNotContain(CertificatePassword, values);
    }

    /// <summary>Losing the values must not lose the fact that a secret is stored, or swapping one kind
    /// of credential for another would leave no trace in the audit trail.</summary>
    [Fact]
    public void View_keeps_the_fact_that_a_secret_is_stored()
    {
        var credential = BuildCredential();

        var withCertificate = KsefCredentialAuditView.From(credential);
        Assert.True(withCertificate.HasCertificate);
        Assert.False(withCertificate.HasToken);

        credential.EncryptedCertificate = null;
        credential.EncryptedCertificatePassword = null;
        credential.EncryptedToken = Token;
        credential.Kind = KsefCredentialKind.Token;

        var withToken = KsefCredentialAuditView.From(credential);
        Assert.False(withToken.HasCertificate);
        Assert.True(withToken.HasToken);
    }

    [Fact]
    public void Label_identifies_the_record_without_opening_it()
    {
        var certificate = KsefCredentialAuditView.From(BuildCredential()).ToString();

        Assert.Contains("firma.p12", certificate);
        Assert.Contains("1234567890", certificate);
        Assert.Contains(nameof(KsefEnvironment.Demo), certificate);
    }

    private static KsefCredential BuildCredential() => new()
    {
        Guid = Guid.NewGuid(),
        CompanyGuid = Guid.NewGuid(),
        Kind = KsefCredentialKind.Certificate,
        Environment = KsefEnvironment.Demo,
        Nip = "1234567890",
        EncryptedCertificate = Certificate,
        EncryptedCertificatePassword = CertificatePassword,
        CertificateFileName = "firma.p12",
        CertificateSubject = "CN=Firma",
        CertificateThumbprint = "AABBCC",
        CertificateValidFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        CertificateValidTo = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        Created = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
    };
}
