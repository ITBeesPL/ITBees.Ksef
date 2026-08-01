namespace ITBees.Ksef.Invoicing;

public interface IFa3XmlGenerator
{
    /// <summary>Renders the invoice to FA(3) XML using the current UTC time as DataWytworzeniaFa.</summary>
    string Generate(KsefInvoice invoice);

    string Generate(KsefInvoice invoice, DateTimeOffset generatedAtUtc);
}
