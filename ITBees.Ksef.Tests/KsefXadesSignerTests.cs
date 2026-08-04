using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using ITBees.Ksef.Security;
using Xunit;

namespace ITBees.Ksef.Tests;

public class KsefXadesSignerTests
{
    private const string XadesNamespace = "http://uri.etsi.org/01903/v1.3.2#";
    private const string DsNamespace = "http://www.w3.org/2000/09/xmldsig#";

    [Fact]
    public void BuildAuthTokenRequest_ContainsChallengeAndContextInAuthNamespace()
    {
        var xml = KsefXadesSigner.BuildAuthTokenRequest("20260803-CR-1A2B3C4D5E-6F7A8B9C0D-11", "5252445761");

        var document = Load(xml);
        var namespaces = CreateNamespaceManager(document);

        Assert.Equal(KsefXadesSigner.AuthNamespace, document.DocumentElement!.NamespaceURI);
        Assert.Equal("AuthTokenRequest", document.DocumentElement.LocalName);
        Assert.Equal("20260803-CR-1A2B3C4D5E-6F7A8B9C0D-11",
            document.SelectSingleNode("/auth:AuthTokenRequest/auth:Challenge", namespaces)?.InnerText);
        Assert.Equal("5252445761",
            document.SelectSingleNode("/auth:AuthTokenRequest/auth:ContextIdentifier/auth:Nip", namespaces)
                ?.InnerText);
        Assert.Equal("certificateSubject",
            document.SelectSingleNode("/auth:AuthTokenRequest/auth:SubjectIdentifierType", namespaces)?.InnerText);
    }

    [Fact]
    public void Sign_ProducesSignatureThatVerifiesWithTheEmbeddedCertificate()
    {
        using var certificate = CreateSelfSignedCertificate();

        var signed = KsefXadesSigner.BuildSignedAuthTokenRequest("challenge-value", "5252445761", certificate);

        var document = Load(signed);
        var signatureElement = (XmlElement)document
            .GetElementsByTagName("Signature", DsNamespace)[0]!;

        var signedXml = new SignedXml(document);
        signedXml.LoadXml(signatureElement);

        // Verifying against the key inside KeyInfo proves both the digest chain and the RSA signature.
        Assert.True(signedXml.CheckSignature(certificate, verifySignatureOnly: true));
    }

    [Fact]
    public void Sign_AddsXadesSignedPropertiesReferencedFromSignedInfo()
    {
        using var certificate = CreateSelfSignedCertificate();
        var signingTime = new DateTimeOffset(2026, 8, 3, 9, 30, 0, TimeSpan.Zero);

        var document = Load(KsefXadesSigner.BuildSignedAuthTokenRequest("challenge-value", "5252445761",
            certificate, signingTime: signingTime));
        var namespaces = CreateNamespaceManager(document);

        var signedProperties = document.SelectSingleNode("//xades:SignedProperties", namespaces) as XmlElement;
        Assert.NotNull(signedProperties);
        Assert.Equal("SignedProperties", signedProperties!.GetAttribute("Id"));

        Assert.Equal("2026-08-03T09:30:00Z",
            document.SelectSingleNode("//xades:SigningTime", namespaces)?.InnerText);

        // The certificate digest must match the actual signing certificate, otherwise KSeF rejects the XAdES.
        Assert.Equal(Convert.ToBase64String(SHA256.HashData(certificate.RawData)),
            document.SelectSingleNode("//xades:CertDigest/ds:DigestValue", namespaces)?.InnerText);

        // Two references: the enveloped document and the SignedProperties block.
        var references = document.SelectNodes("//ds:SignedInfo/ds:Reference", namespaces);
        Assert.Equal(2, references!.Count);
        Assert.Contains(references.Cast<XmlElement>(), r => r.GetAttribute("URI") == "#SignedProperties");
    }

    [Fact]
    public void Sign_WithEcdsaCertificate_ProducesVerifiableEcdsaSha256Signature()
    {
        // KSeF-issued certificates (CCK KSeF) carry EC keys, so this path is what production uses.
        using var certificate = CreateSelfSignedEcdsaCertificate();

        var signed = KsefXadesSigner.BuildSignedAuthTokenRequest("challenge-value", "5252445761", certificate);

        var document = Load(signed);
        var namespaces = CreateNamespaceManager(document);

        Assert.Equal("http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256",
            (document.SelectSingleNode("//ds:SignedInfo/ds:SignatureMethod", namespaces) as XmlElement)
            ?.GetAttribute("Algorithm"));

        var signedXml = new SignedXml(document);
        signedXml.LoadXml((XmlElement)document.GetElementsByTagName("Signature", DsNamespace)[0]!);
        Assert.True(signedXml.CheckSignature(certificate, verifySignatureOnly: true));
    }

    [Fact]
    public void Sign_ThrowsWhenCertificateHasNoPrivateKey()
    {
        using var withKey = CreateSelfSignedCertificate();
        using var publicOnly = new X509Certificate2(withKey.Export(X509ContentType.Cert));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            KsefXadesSigner.Sign("<AuthTokenRequest xmlns=\"" + KsefXadesSigner.AuthNamespace + "\" />",
                publicOnly));

        Assert.Contains("private key", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static X509Certificate2 CreateSelfSignedEcdsaCertificate()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Jan Testowy, SERIALNUMBER=PNOPL-82040510152, C=PL",
            ecdsa, HashAlgorithmName.SHA256);

        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        return new X509Certificate2(certificate.Export(X509ContentType.Pkcs12, "pwd"), "pwd",
            X509KeyStorageFlags.EphemeralKeySet);
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Testowa Firma Sp. z o.o., SERIALNUMBER=NIP-5252445761, C=PL",
            rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        // Round-tripping through PKCS#12 mirrors how the app loads a certificate uploaded by the user.
        return new X509Certificate2(certificate.Export(X509ContentType.Pkcs12, "pwd"), "pwd",
            X509KeyStorageFlags.EphemeralKeySet);
    }

    private static XmlDocument Load(string xml)
    {
        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(xml);
        return document;
    }

    private static XmlNamespaceManager CreateNamespaceManager(XmlDocument document)
    {
        var namespaces = new XmlNamespaceManager(document.NameTable);
        namespaces.AddNamespace("auth", KsefXadesSigner.AuthNamespace);
        namespaces.AddNamespace("xades", XadesNamespace);
        namespaces.AddNamespace("ds", DsNamespace);
        return namespaces;
    }
}
