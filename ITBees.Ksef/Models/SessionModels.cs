using System.Text.Json.Serialization;

namespace ITBees.Ksef.Models;

public class FormCode
{
    public string SystemCode { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    /// <summary>FA(3) — the only schema accepted by DEMO/PROD since 2026.</summary>
    public static FormCode Fa3 { get; } = new() { SystemCode = "FA (3)", SchemaVersion = "1-0E", Value = "FA" };

    /// <summary>FA(2) — accepted on the TEST environment only.</summary>
    public static FormCode Fa2 { get; } = new() { SystemCode = "FA (2)", SchemaVersion = "1-0E", Value = "FA" };
}

public class EncryptionInfo
{
    /// <summary>Base64 of the AES-256 key encrypted with RSAES-OAEP (SHA-256/MGF1) using the KSeF public key.</summary>
    public string EncryptedSymmetricKey { get; set; } = string.Empty;
    /// <summary>Base64 of the 16-byte AES initialization vector.</summary>
    public string InitializationVector { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PublicKeyId { get; set; }
}

public class OpenOnlineSessionRequest
{
    public FormCode FormCode { get; set; } = FormCode.Fa3;
    public EncryptionInfo Encryption { get; set; } = new();
}

public class OpenOnlineSessionResponse
{
    public string ReferenceNumber { get; set; } = string.Empty;
    public DateTimeOffset ValidUntil { get; set; }
}

public class SendInvoiceRequest
{
    /// <summary>Base64 SHA-256 hash of the plaintext invoice XML.</summary>
    public string InvoiceHash { get; set; } = string.Empty;
    public long InvoiceSize { get; set; }
    /// <summary>Base64 SHA-256 hash of the encrypted invoice payload.</summary>
    public string EncryptedInvoiceHash { get; set; } = string.Empty;
    public long EncryptedInvoiceSize { get; set; }
    /// <summary>Base64 of the invoice XML encrypted with AES-256-CBC (PKCS#7) using the session key.</summary>
    public string EncryptedInvoiceContent { get; set; } = string.Empty;
    public bool OfflineMode { get; set; }
}

public class SendInvoiceResponse
{
    public string ReferenceNumber { get; set; } = string.Empty;
}

public class SessionStatusResponse
{
    public StatusInfo Status { get; set; } = new();
    public DateTimeOffset DateCreated { get; set; }
    public DateTimeOffset DateUpdated { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public int? InvoiceCount { get; set; }
    public int? SuccessfulInvoiceCount { get; set; }
    public int? FailedInvoiceCount { get; set; }
    public UpoResponse? Upo { get; set; }
}

public class UpoResponse
{
    public List<UpoPageResponse> Pages { get; set; } = new();
}

public class UpoPageResponse
{
    public string? ReferenceNumber { get; set; }
    public string? DownloadUrl { get; set; }
}

public class InvoiceStatusInfo : StatusInfo
{
}

public class SessionInvoiceStatusResponse
{
    public int OrdinalNumber { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? KsefNumber { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string InvoiceHash { get; set; } = string.Empty;
    public DateTimeOffset? AcquisitionDate { get; set; }
    public DateTimeOffset InvoicingDate { get; set; }
    public DateTimeOffset? PermanentStorageDate { get; set; }
    public string? UpoDownloadUrl { get; set; }
    public InvoiceStatusInfo Status { get; set; } = new();
}
