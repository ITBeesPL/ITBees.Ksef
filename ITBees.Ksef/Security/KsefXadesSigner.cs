using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace ITBees.Ksef.Security;

/// <summary>
/// Builds and signs the <c>AuthTokenRequest</c> document consumed by <c>POST /auth/xades-signature</c>.
/// The signature is an enveloped XAdES-BES: two references (whole document + SignedProperties),
/// RSA-SHA256, exclusive canonicalization, certificate carried in KeyInfo/X509Data.
/// </summary>
public static class KsefXadesSigner
{
    public const string AuthNamespace = "http://ksef.mf.gov.pl/auth/token/2.0";

    private const string XadesNamespace = "http://uri.etsi.org/01903/v1.3.2#";
    private const string SignedPropertiesType = "http://uri.etsi.org/01903#SignedProperties";
    private const string RsaSha256 = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";
    private const string Sha256 = "http://www.w3.org/2001/04/xmlenc#sha256";
    private const string SignatureId = "Signature";
    private const string SignedPropertiesId = "SignedProperties";

    /// <summary>
    /// Renders the unsigned AuthTokenRequest for a challenge returned by <c>POST /auth/challenge</c>.
    /// </summary>
    /// <param name="challenge">Challenge value from the auth challenge response.</param>
    /// <param name="contextIdentifierValue">NIP of the context the caller wants to act in.</param>
    /// <param name="contextIdentifierType">"Nip", "InternalId", "NipVatUe" or "PeppolId".</param>
    public static string BuildAuthTokenRequest(string challenge, string contextIdentifierValue,
        string contextIdentifierType = "Nip")
    {
        if (string.IsNullOrWhiteSpace(challenge))
            throw new ArgumentException("Challenge is required.", nameof(challenge));
        if (string.IsNullOrWhiteSpace(contextIdentifierValue))
            throw new ArgumentException("Context identifier is required.", nameof(contextIdentifierValue));

        var document = new XmlDocument { PreserveWhitespace = true };
        var root = document.CreateElement("AuthTokenRequest", AuthNamespace);
        document.AppendChild(root);

        root.AppendChild(CreateTextElement(document, "Challenge", challenge));

        var context = document.CreateElement("ContextIdentifier", AuthNamespace);
        context.AppendChild(CreateTextElement(document, contextIdentifierType, contextIdentifierValue));
        root.AppendChild(context);

        // The certificate itself identifies the subject; KSeF reads the NIP/PESEL from its subject fields.
        root.AppendChild(CreateTextElement(document, "SubjectIdentifierType", "certificateSubject"));

        return ToXmlString(document);
    }

    /// <summary>
    /// Signs the given XML in place with an enveloped XAdES-BES signature.
    /// </summary>
    /// <param name="xml">Document to sign (typically the result of <see cref="BuildAuthTokenRequest"/>).</param>
    /// <param name="certificate">Certificate with an accessible RSA private key.</param>
    /// <param name="signingTime">Value of xades:SigningTime; defaults to now (UTC).</param>
    public static string Sign(string xml, X509Certificate2 certificate, DateTimeOffset? signingTime = null)
    {
        if (!certificate.HasPrivateKey)
            throw new InvalidOperationException(
                "The KSeF signing certificate does not contain a private key — export it as .p12/.pfx including the key.");

        using var rsa = certificate.GetRSAPrivateKey()
                        ?? throw new InvalidOperationException(
                            "Only RSA certificates are supported for the KSeF XAdES authentication.");

        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(xml);

        var signedXml = new XadesSignedXml(document) { SigningKey = rsa };
        signedXml.Signature.Id = SignatureId;
        signedXml.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
        signedXml.SignedInfo.SignatureMethod = RsaSha256;

        var documentReference = new Reference(string.Empty) { DigestMethod = Sha256 };
        documentReference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        documentReference.AddTransform(new XmlDsigExcC14NTransform());
        signedXml.AddReference(documentReference);

        signedXml.AddObject(BuildQualifyingPropertiesObject(document, certificate,
            signingTime ?? DateTimeOffset.UtcNow));

        var propertiesReference = new Reference($"#{SignedPropertiesId}")
        {
            DigestMethod = Sha256,
            Type = SignedPropertiesType
        };
        propertiesReference.AddTransform(new XmlDsigExcC14NTransform());
        signedXml.AddReference(propertiesReference);

        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(certificate));
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();
        document.DocumentElement!.AppendChild(document.ImportNode(signedXml.GetXml(), true));

        return ToXmlString(document);
    }

    /// <summary>Convenience wrapper: build the request for a challenge and sign it in one call.</summary>
    public static string BuildSignedAuthTokenRequest(string challenge, string contextIdentifierValue,
        X509Certificate2 certificate, string contextIdentifierType = "Nip", DateTimeOffset? signingTime = null) =>
        Sign(BuildAuthTokenRequest(challenge, contextIdentifierValue, contextIdentifierType), certificate,
            signingTime);

    private static DataObject BuildQualifyingPropertiesObject(XmlDocument document, X509Certificate2 certificate,
        DateTimeOffset signingTime)
    {
        var qualifyingProperties = document.CreateElement("xades", "QualifyingProperties", XadesNamespace);
        qualifyingProperties.SetAttribute("Target", $"#{SignatureId}");

        var signedProperties = document.CreateElement("xades", "SignedProperties", XadesNamespace);
        // SignedXml resolves the "#SignedProperties" reference by this attribute — the name must stay "Id".
        signedProperties.SetAttribute("Id", SignedPropertiesId);
        qualifyingProperties.AppendChild(signedProperties);

        var signatureProperties = document.CreateElement("xades", "SignedSignatureProperties", XadesNamespace);
        signedProperties.AppendChild(signatureProperties);

        var signingTimeElement = document.CreateElement("xades", "SigningTime", XadesNamespace);
        signingTimeElement.AppendChild(document.CreateTextNode(
            signingTime.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        signatureProperties.AppendChild(signingTimeElement);

        signatureProperties.AppendChild(BuildSigningCertificateElement(document, certificate));

        // DataObject.Data needs an XmlNodeList; a fragment is the cheapest way to produce one.
        var fragment = document.CreateDocumentFragment();
        fragment.AppendChild(qualifyingProperties);

        return new DataObject { Data = fragment.ChildNodes };
    }

    private static XmlElement BuildSigningCertificateElement(XmlDocument document, X509Certificate2 certificate)
    {
        var signingCertificate = document.CreateElement("xades", "SigningCertificate", XadesNamespace);
        var cert = document.CreateElement("xades", "Cert", XadesNamespace);
        signingCertificate.AppendChild(cert);

        var certDigest = document.CreateElement("xades", "CertDigest", XadesNamespace);
        cert.AppendChild(certDigest);

        var digestMethod = document.CreateElement("ds", "DigestMethod", SignedXml.XmlDsigNamespaceUrl);
        digestMethod.SetAttribute("Algorithm", Sha256);
        certDigest.AppendChild(digestMethod);

        var digestValue = document.CreateElement("ds", "DigestValue", SignedXml.XmlDsigNamespaceUrl);
        digestValue.AppendChild(document.CreateTextNode(
            Convert.ToBase64String(SHA256.HashData(certificate.RawData))));
        certDigest.AppendChild(digestValue);

        var issuerSerial = document.CreateElement("xades", "IssuerSerial", XadesNamespace);
        cert.AppendChild(issuerSerial);

        var issuerName = document.CreateElement("ds", "X509IssuerName", SignedXml.XmlDsigNamespaceUrl);
        issuerName.AppendChild(document.CreateTextNode(certificate.IssuerName.Name));
        issuerSerial.AppendChild(issuerName);

        var serialNumber = document.CreateElement("ds", "X509SerialNumber", SignedXml.XmlDsigNamespaceUrl);
        serialNumber.AppendChild(document.CreateTextNode(ToDecimalSerialNumber(certificate.SerialNumber)));
        issuerSerial.AppendChild(serialNumber);

        return signingCertificate;
    }

    /// <summary>X509SerialNumber is defined as an integer, while X509Certificate2.SerialNumber is hex.</summary>
    private static string ToDecimalSerialNumber(string hexSerialNumber)
    {
        // Prepending "0" keeps BigInteger from reading the high bit as a sign — serial numbers are unsigned.
        var value = BigInteger.Parse("0" + hexSerialNumber, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static XmlElement CreateTextElement(XmlDocument document, string name, string value)
    {
        var element = document.CreateElement(name, AuthNamespace);
        element.AppendChild(document.CreateTextNode(value));
        return element;
    }

    /// <summary>
    /// SignedXml only resolves same-document references against the document and against
    /// <c>ds:Object/@Id</c>. XAdES points at <c>xades:SignedProperties</c>, which sits *inside* the
    /// object — without this override ComputeSignature fails with "Malformed reference element".
    /// </summary>
    private sealed class XadesSignedXml : SignedXml
    {
        public XadesSignedXml(XmlDocument document) : base(document)
        {
        }

        public override XmlElement? GetIdElement(XmlDocument? document, string idValue)
        {
            var fromDocument = base.GetIdElement(document, idValue);
            if (fromDocument != null)
                return fromDocument;

            foreach (DataObject dataObject in Signature.ObjectList)
            {
                var match = FindById(dataObject.GetXml(), idValue);
                if (match != null)
                    return match;
            }

            return null;
        }

        private static XmlElement? FindById(XmlNode? node, string idValue)
        {
            if (node is null)
                return null;

            if (node is XmlElement element && element.GetAttribute("Id") == idValue)
                return element;

            foreach (XmlNode child in node.ChildNodes)
            {
                var match = FindById(child, idValue);
                if (match != null)
                    return match;
            }

            return null;
        }
    }

    private static string ToXmlString(XmlDocument document)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            OmitXmlDeclaration = false
        };

        using var buffer = new MemoryStream();
        using (var writer = XmlWriter.Create(buffer, settings))
        {
            document.Save(writer);
        }

        return new UTF8Encoding(false).GetString(buffer.ToArray());
    }
}
