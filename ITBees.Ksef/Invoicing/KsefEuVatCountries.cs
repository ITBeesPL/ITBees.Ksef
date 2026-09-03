namespace ITBees.Ksef.Invoicing;

/// <summary>
/// VAT prefixes accepted in KodUE (TKodyKrajowUE in the FA(3) schema). This is the VIES list,
/// not ISO 3166: Greece is "EL" and Northern Ireland "XI" (post-Brexit goods trade under the
/// Windsor Framework), while "GB" is not a member.
/// </summary>
public static class KsefEuVatCountries
{
    public static readonly IReadOnlySet<string> Codes = new HashSet<string>(StringComparer.Ordinal)
    {
        "AT", "BE", "BG", "CY", "CZ", "DK", "EE", "FI", "FR", "DE", "EL", "HR", "HU", "IE", "IT",
        "LV", "LT", "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE", "XI"
    };

    /// <summary>True when the code is a VAT prefix of an EU member state (case-insensitive; "GR" is mapped to "EL").</summary>
    public static bool Contains(string? code) => !string.IsNullOrWhiteSpace(code) && Codes.Contains(ToVatPrefix(code));

    /// <summary>ISO country code → VIES VAT prefix ("GR" → "EL"); everything else upper-cased as is.</summary>
    public static string ToVatPrefix(string code)
    {
        var upper = code.Trim().ToUpperInvariant();
        return upper == "GR" ? "EL" : upper;
    }
}
