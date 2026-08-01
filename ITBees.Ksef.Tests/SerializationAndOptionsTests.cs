using System.Text.Json;
using ITBees.Ksef.Configuration;
using ITBees.Ksef.Http;
using ITBees.Ksef.Models;
using Xunit;

namespace ITBees.Ksef.Tests;

public class SerializationAndOptionsTests
{
    [Theory]
    [InlineData(KsefEnvironment.Test, "https://api-test.ksef.mf.gov.pl/v2")]
    [InlineData(KsefEnvironment.Demo, "https://api-demo.ksef.mf.gov.pl/v2")]
    [InlineData(KsefEnvironment.Production, "https://api.ksef.mf.gov.pl/v2")]
    public void GetBaseUrl_ResolvesEnvironment(KsefEnvironment environment, string expected)
    {
        Assert.Equal(expected, new KsefOptions { Environment = environment }.GetBaseUrl());
    }

    [Fact]
    public void GetBaseUrl_ExplicitOverrideWins()
    {
        var options = new KsefOptions { Environment = KsefEnvironment.Production, BaseUrl = "https://localhost:5001/v2/" };
        Assert.Equal("https://localhost:5001/v2", options.GetBaseUrl());
    }

    [Fact]
    public void SendInvoiceRequest_SerializesToCamelCase()
    {
        var request = new SendInvoiceRequest
        {
            InvoiceHash = "hash",
            InvoiceSize = 10,
            EncryptedInvoiceHash = "ehash",
            EncryptedInvoiceSize = 16,
            EncryptedInvoiceContent = "content"
        };

        var json = JsonSerializer.Serialize(request, KsefApiClient.JsonOptions);

        Assert.Contains("\"invoiceHash\":\"hash\"", json);
        Assert.Contains("\"encryptedInvoiceContent\":\"content\"", json);
        Assert.Contains("\"offlineMode\":false", json);
    }

    [Fact]
    public void InitTokenAuthenticationRequest_OmitsNullPublicKeyId()
    {
        var request = new InitTokenAuthenticationRequest
        {
            Challenge = "c",
            ContextIdentifier = new ContextIdentifier { Type = "Nip", Value = "1111111111" },
            EncryptedToken = "e"
        };

        var json = JsonSerializer.Serialize(request, KsefApiClient.JsonOptions);

        Assert.DoesNotContain("publicKeyId", json);
        Assert.Contains("\"type\":\"Nip\"", json);
    }

    [Fact]
    public void SessionInvoiceStatusResponse_DeserializesFromApiShape()
    {
        const string json = """
            {
              "ordinalNumber": 1,
              "invoiceNumber": "FV/1",
              "ksefNumber": "1111111111-20260801-ABCDEF012345-01",
              "referenceNumber": "ref-123",
              "invoiceHash": "aGFzaA==",
              "invoicingDate": "2026-08-01T10:00:00+00:00",
              "status": { "code": 200, "description": "Faktura przyjęta", "details": null }
            }
            """;

        var status = JsonSerializer.Deserialize<SessionInvoiceStatusResponse>(json, KsefApiClient.JsonOptions)!;

        Assert.Equal("1111111111-20260801-ABCDEF012345-01", status.KsefNumber);
        Assert.Equal(200, status.Status.Code);
    }

    [Fact]
    public void FormCodeFa3_HasOfficialValues()
    {
        Assert.Equal("FA (3)", FormCode.Fa3.SystemCode);
        Assert.Equal("1-0E", FormCode.Fa3.SchemaVersion);
        Assert.Equal("FA", FormCode.Fa3.Value);
    }
}
