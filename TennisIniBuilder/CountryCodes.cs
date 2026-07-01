namespace TennisIniBuilder;

/// <summary>
/// Normalizes the IOC/ITF nation codes that the WTA/ATP feeds emit into the
/// ISO 3166-1 alpha-3 codes that the Players.ini / game country table expects.
///
/// The tennis feeds use IOC-style abbreviations (e.g. SUI, GER, NED) which differ
/// from ISO alpha-3 (CHE, DEU, NLD) for a fixed set of countries. Codes that are
/// already valid ISO alpha-3 (USA, RUS, ESP, ...) are passed through untouched.
/// </summary>
public static class CountryCodes
{
    // Kosovo has no ISO 3166-1 code (XK/XKX is only "user-assigned"). The game
    // country table has no Kosovo entry, so it falls back to Albania (ALB) — the
    // standard proxy used across sports datasets (shared language/ethnicity).
    private const string KosovoFallback = "ALB";

    /// <summary>IOC/ITF code -> ISO 3166-1 alpha-3. Only codes that actually differ are listed.</summary>
    private static readonly Dictionary<string, string> IocToIso = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ALG"] = "DZA", // Algeria
        ["ANG"] = "AGO", // Angola
        ["ARU"] = "ABW", // Aruba
        ["BAH"] = "BHS", // Bahamas
        ["BAN"] = "BGD", // Bangladesh
        ["BAR"] = "BRB", // Barbados
        ["BER"] = "BMU", // Bermuda
        ["BHU"] = "BTN", // Bhutan
        ["BIZ"] = "BLZ", // Belize
        ["BOT"] = "BWA", // Botswana
        ["BRU"] = "BRN", // Brunei
        ["BUL"] = "BGR", // Bulgaria
        ["BUR"] = "BFA", // Burkina Faso
        ["CAM"] = "KHM", // Cambodia
        ["CGO"] = "COG", // Congo (Republic)
        ["CHA"] = "TCD", // Chad
        ["CHI"] = "CHL", // Chile
        ["CRC"] = "CRI", // Costa Rica
        ["CRO"] = "HRV", // Croatia
        ["DEN"] = "DNK", // Denmark
        ["ESA"] = "SLV", // El Salvador
        ["GAM"] = "GMB", // Gambia
        ["GER"] = "DEU", // Germany
        ["GRE"] = "GRC", // Greece
        ["GRN"] = "GRD", // Grenada
        ["GUA"] = "GTM", // Guatemala
        ["GUI"] = "GIN", // Guinea
        ["HAI"] = "HTI", // Haiti
        ["HON"] = "HND", // Honduras
        ["INA"] = "IDN", // Indonesia
        ["IRI"] = "IRN", // Iran
        ["KSA"] = "SAU", // Saudi Arabia
        ["KUW"] = "KWT", // Kuwait
        ["LAT"] = "LVA", // Latvia
        ["LES"] = "LSO", // Lesotho
        ["LIB"] = "LBN", // Lebanon
        ["MAD"] = "MDG", // Madagascar
        ["MAS"] = "MYS", // Malaysia
        ["MAW"] = "MWI", // Malawi
        ["MGL"] = "MNG", // Mongolia
        ["MON"] = "MCO", // Monaco
        ["MRI"] = "MUS", // Mauritius
        ["MTN"] = "MRT", // Mauritania
        ["MYA"] = "MMR", // Myanmar
        ["NCA"] = "NIC", // Nicaragua
        ["NED"] = "NLD", // Netherlands
        ["NEP"] = "NPL", // Nepal
        ["NGR"] = "NGA", // Nigeria
        ["NIG"] = "NER", // Niger
        ["NMI"] = "MNP", // Northern Mariana Islands
        ["OMA"] = "OMN", // Oman
        ["PAR"] = "PRY", // Paraguay
        ["PHI"] = "PHL", // Philippines
        ["POR"] = "PRT", // Portugal
        ["PUR"] = "PRI", // Puerto Rico
        ["RSA"] = "ZAF", // South Africa
        ["SAM"] = "WSM", // Samoa
        ["SEY"] = "SYC", // Seychelles
        ["SIN"] = "SGP", // Singapore
        ["SLO"] = "SVN", // Slovenia
        ["SOL"] = "SLB", // Solomon Islands
        ["SRI"] = "LKA", // Sri Lanka
        ["SUD"] = "SDN", // Sudan
        ["SUI"] = "CHE", // Switzerland
        ["TAN"] = "TZA", // Tanzania
        ["TGA"] = "TON", // Tonga
        ["TOG"] = "TGO", // Togo
        ["TPE"] = "TWN", // Chinese Taipei / Taiwan
        ["UAE"] = "ARE", // United Arab Emirates
        ["URU"] = "URY", // Uruguay
        ["VAN"] = "VUT", // Vanuatu
        ["VIE"] = "VNM", // Vietnam
        ["VIN"] = "VCT", // St Vincent & the Grenadines
        ["ZAM"] = "ZMB", // Zambia
        ["ZIM"] = "ZWE", // Zimbabwe

        // Kosovo variants (no ISO code) -> Albania fallback.
        ["KOS"] = KosovoFallback,
        ["XKX"] = KosovoFallback,
        ["XK"] = KosovoFallback,
    };

    /// <summary>
    /// Returns the ISO 3166-1 alpha-3 code for a feed country code. Already-valid
    /// ISO codes pass through unchanged; empty/unknown input returns the trimmed,
    /// upper-cased original so nothing is silently dropped.
    /// </summary>
    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        string c = code.Trim().ToUpperInvariant();
        return IocToIso.TryGetValue(c, out var iso) ? iso : c;
    }
}
