namespace ITBees.Ksef.Core;

public interface IInvoiceDocument
{
    Guid Id { get; }
    IssuerId Issuer { get; }
    string LocalNumber { get; }        // Twój numer
    string XmlRaw { get; }             // surowe FA(2)/FA(3)
    DateTime CreatedAt { get; }
}