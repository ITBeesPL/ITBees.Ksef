using System.Text.Json.Serialization;

namespace ITBees.Ksef.Models;

public class AuthenticationChallengeResponse
{
    public string Challenge { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public long TimestampMs { get; set; }
}

public class ContextIdentifier
{
    /// <summary>"Nip", "InternalId", "NipVatUe" or "PeppolId".</summary>
    public string Type { get; set; } = "Nip";
    public string Value { get; set; } = string.Empty;
}

public class InitTokenAuthenticationRequest
{
    public string Challenge { get; set; } = string.Empty;
    public ContextIdentifier ContextIdentifier { get; set; } = new();
    /// <summary>Base64 of RSA-OAEP(SHA-256) encrypted "{ksefToken}|{challengeTimestampMs}".</summary>
    public string EncryptedToken { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PublicKeyId { get; set; }
}

public class TokenInfo
{
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ValidUntil { get; set; }
}

public class AuthenticationInitResponse
{
    public string ReferenceNumber { get; set; } = string.Empty;
    public TokenInfo AuthenticationToken { get; set; } = new();
}

public class StatusInfo
{
    public int Code { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<string>? Details { get; set; }

    public override string ToString()
    {
        var details = Details is { Count: > 0 } ? $" ({string.Join("; ", Details)})" : string.Empty;
        return $"{Code} {Description}{details}";
    }
}

public class AuthenticationOperationStatusResponse
{
    public DateTimeOffset StartDate { get; set; }
    public StatusInfo Status { get; set; } = new();
    public bool? IsTokenRedeemed { get; set; }
}

public class AuthenticationTokensResponse
{
    public TokenInfo AccessToken { get; set; } = new();
    public TokenInfo RefreshToken { get; set; } = new();
}

public class AuthenticationTokenRefreshResponse
{
    public TokenInfo AccessToken { get; set; } = new();
}

public class PublicKeyCertificate
{
    /// <summary>Base64 encoded X.509 (DER) certificate.</summary>
    public string Certificate { get; set; } = string.Empty;
    public string CertificateId { get; set; } = string.Empty;
    public string PublicKeyId { get; set; } = string.Empty;
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset ValidTo { get; set; }
    public List<string> Usage { get; set; } = new();
}

public static class PublicKeyCertificateUsage
{
    public const string KsefTokenEncryption = "KsefTokenEncryption";
    public const string SymmetricKeyEncryption = "SymmetricKeyEncryption";
}
