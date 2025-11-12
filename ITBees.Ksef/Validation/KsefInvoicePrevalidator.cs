using ITBees.Ksef.Core;
using System.Text.RegularExpressions;
using System.Xml.Schema;
using System.Xml;

namespace ITBees.Ksef.Validation;

public sealed class KsefInvoicePrevalidator : IInvoicePrevalidator
{
    private readonly XmlSchemaSet _schemas;
    public KsefInvoicePrevalidator(XmlSchemaSet schemas) => _schemas = schemas;

    public Task<ValidationResult> ValidateAsync(string xml, CancellationToken ct)
    {
        var errors = new List<string>();
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = _schemas };
        settings.ValidationEventHandler += (_, e) => errors.Add(e.Message);

        using var r = XmlReader.Create(new StringReader(xml), settings);
        while (r.Read()) { /* parse to trigger XSD */ }

        // Example business rules (extend as needed)
        // 1) Basic NIP check (10 digits)
        var nipRegex = new Regex(@"<NIP>(\d{10})</NIP>");
        if (!nipRegex.IsMatch(xml)) errors.Add("Missing or invalid NIP (10 digits).");

        // 2) Non-negative line totals example
        if (xml.Contains("<Razem>") && xml.Contains("<WartoscFaktury>") && xml.Contains("-"))
            errors.Add("Negative totals are not allowed.");

        return Task.FromResult(new ValidationResult(errors.Count == 0, errors));
    }
}