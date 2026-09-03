namespace ITBees.Ksef.Invoicing;

/// <summary>
/// What the VAT rate on a line means. FA(3) does not accept a bare "0" in P_12 — the schema
/// (TStawkaPodatku) splits the untaxed cases into distinct codes, each with its own aggregate
/// field in the Fa node, so the percentage alone cannot describe a line.
/// </summary>
public enum KsefVatRateKind
{
    /// <summary>
    /// Regular percentage rate (23, 22, 8, 7, 5, 4). With <c>VatRate = 0</c> it is the domestic
    /// 0% rate — P_12 "0 KR", aggregate P_13_6_1.
    /// </summary>
    Standard = 0,

    /// <summary>0% on an intra-community supply of goods (WDT) — P_12 "0 WDT", aggregate P_13_6_2.</summary>
    ZeroIntraCommunity = 1,

    /// <summary>0% on export of goods outside the EU — P_12 "0 EX", aggregate P_13_6_3.</summary>
    ZeroExport = 2,

    /// <summary>
    /// Exempt from VAT — P_12 "zw", aggregate P_13_7. Requires the legal basis of the exemption
    /// (<see cref="KsefInvoice.ExemptionBasis"/>), emitted as Adnotacje/Zwolnienie/P_19A.
    /// </summary>
    Exempt = 3,

    /// <summary>
    /// Domestic reverse charge — the buyer settles the tax (art. 17 ust. 1 pkt 7 i 8).
    /// P_12 "oo", aggregate P_13_10, Adnotacje/P_18 = 1.
    /// </summary>
    ReverseCharge = 4,

    /// <summary>
    /// Not subject to Polish VAT: supply of goods or services with the place of supply outside
    /// Poland, other than services covered by art. 100 ust. 1 pkt 4 (typically a non-EU
    /// business customer). P_12 "np I", aggregate P_13_8.
    /// </summary>
    NotSubjectNonEu = 5,

    /// <summary>
    /// Not subject to Polish VAT: services for an EU business customer reported in the VAT-UE
    /// summary (art. 100 ust. 1 pkt 4). P_12 "np II", aggregate P_13_9.
    /// </summary>
    NotSubjectEu = 6
}

/// <summary>Translation between the FA(3) P_12 codes and the (rate, kind) pair used by the model.</summary>
public static class KsefVatRates
{
    /// <summary>Percentage rates the generator knows how to aggregate (P_13_1…P_13_3, P_13_6_1).</summary>
    public static readonly int[] SupportedStandardRates = { 23, 22, 8, 7, 5, 4, 0 };

    /// <summary>True when the kind carries a percentage that produces tax; all other kinds yield zero VAT.</summary>
    public static bool IsTaxed(KsefVatRateKind kind) => kind == KsefVatRateKind.Standard;

    /// <summary>Value of P_12 / P_12Z for the pair.</summary>
    public static string ToP12(int rate, KsefVatRateKind kind) => kind switch
    {
        KsefVatRateKind.Standard when rate == 0 => "0 KR",
        KsefVatRateKind.Standard => rate.ToString(System.Globalization.CultureInfo.InvariantCulture),
        KsefVatRateKind.ZeroIntraCommunity => "0 WDT",
        KsefVatRateKind.ZeroExport => "0 EX",
        KsefVatRateKind.Exempt => "zw",
        KsefVatRateKind.ReverseCharge => "oo",
        KsefVatRateKind.NotSubjectNonEu => "np I",
        KsefVatRateKind.NotSubjectEu => "np II",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown VAT rate kind.")
    };

    /// <summary>
    /// Reads a P_12 value back into the pair. Unknown or empty text yields (0, Standard) — the
    /// safest reading of a document produced by somebody else's generator; percentages that are
    /// not on the FA list still come back as their number so nothing is silently lost.
    /// </summary>
    public static (int Rate, KsefVatRateKind Kind) Parse(string? p12)
    {
        var text = (p12 ?? string.Empty).Trim();
        switch (text.ToUpperInvariant())
        {
            case "0 WDT": return (0, KsefVatRateKind.ZeroIntraCommunity);
            case "0 EX": return (0, KsefVatRateKind.ZeroExport);
            case "0 KR": return (0, KsefVatRateKind.Standard);
            case "ZW": return (0, KsefVatRateKind.Exempt);
            case "OO": return (0, KsefVatRateKind.ReverseCharge);
            case "NP I": return (0, KsefVatRateKind.NotSubjectNonEu);
            case "NP II": return (0, KsefVatRateKind.NotSubjectEu);
            // Older schemas (FA(1)) and hand-written documents use plain "np" — the non-EU reading
            // is the one that does not put anything into the VAT-UE summary by mistake.
            case "NP": return (0, KsefVatRateKind.NotSubjectNonEu);
        }

        var digits = new string(text.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, System.Globalization.NumberStyles.Integer,
                   System.Globalization.CultureInfo.InvariantCulture, out var rate) && rate is >= 0 and <= 100
            ? (rate, KsefVatRateKind.Standard)
            : (0, KsefVatRateKind.Standard);
    }
}
