using System.Globalization;
using System.Text;

namespace TennisIniBuilder;

/// <summary>
/// Deterministic Players.ini builder. Faithful port of build_ini.py, generalized
/// so the "included" set and RankPerYear span an arbitrary [yearA, yearB] range.
/// </summary>
public static class IniBuilder
{
    private static readonly string[] Styles =
    {
        "Defender", "PowerBaseliner", "Puncher", "Varied", "Volleyer",
        "Counter", "CounterPuncher", "AllRounder", "Bulldog",
    };

    private static readonly Dictionary<string, Dictionary<string, int>> StyleUp = new()
    {
        ["Defender"] = new() { ["Forehand_Consistency"] = 18, ["Backhand_Consistency"] = 18, ["Passing"] = 15, ["Speed"] = 15, ["Stamina"] = 18, ["Tonicity"] = 10, ["Reflexes"] = 12, ["Tactic"] = 15, ["Positioning"] = 15, ["Motivation"] = 10 },
        ["PowerBaseliner"] = new() { ["Forehand_Power"] = 20, ["Backhand_Consistency"] = 15, ["Forehand_Precision"] = 10, ["Topspin"] = 12, ["Positioning"] = 12, ["SelfEsteem"] = 12, ["Strength"] = 10 },
        ["Puncher"] = new() { ["Forehand_Power"] = 18, ["Backhand_Consistency"] = 14, ["ForehandVolley"] = 12, ["BackhandVolley"] = 10, ["Positioning"] = 12, ["SelfEsteem"] = 12 },
        ["Varied"] = new() { ["Forehand_Power"] = 12, ["ForehandVolley"] = 12, ["Service_Power"] = 12, ["NetPresence"] = 12, ["Tactic"] = 14, ["Positioning"] = 14, ["SelfEsteem"] = 12 },
        ["Volleyer"] = new() { ["ForehandVolley"] = 20, ["BackhandVolley"] = 18, ["Smash"] = 14, ["NetPresence"] = 20, ["Service_Power"] = 12, ["Positioning"] = 14, ["SelfEsteem"] = 12, ["Motivation"] = 10 },
        ["Counter"] = new() { ["Forehand_Consistency"] = 14, ["Backhand_Consistency"] = 14, ["Passing"] = 16, ["Counter"] = 20, ["Lob"] = 12, ["Speed"] = 14, ["Tactic"] = 15, ["Positioning"] = 14, ["SelfEsteem"] = 12 },
        ["CounterPuncher"] = new() { ["Forehand_Consistency"] = 14, ["Backhand_Consistency"] = 14, ["Counter"] = 16, ["Speed"] = 14, ["Tactic"] = 14, ["Positioning"] = 14, ["SelfEsteem"] = 12 },
        ["AllRounder"] = new() { ["Forehand_Consistency"] = 8, ["Backhand_Consistency"] = 8, ["ForehandVolley"] = 8, ["Service_Consistency"] = 8, ["Tactic"] = 14, ["Positioning"] = 14, ["SelfEsteem"] = 12 },
        ["Bulldog"] = new() { ["Forehand_Consistency"] = 14, ["Backhand_Consistency"] = 14, ["Forehand_Power"] = 14, ["Speed"] = 14, ["Stamina"] = 16, ["Tactic"] = 14, ["Positioning"] = 12, ["Motivation"] = 12 },
    };

    private static readonly string[] Skills =
    {
        "Forehand_Power", "Forehand_Consistency", "Forehand_Precision",
        "Backhand_Power", "Backhand_Consistency", "Backhand_Precision",
        "Service_Power", "Service_Consistency", "Service_Precision",
        "Return", "Lob", "Passing", "Dropshot", "Counter",
        "ForehandVolley", "BackhandVolley", "Smash", "NetPresence", "Topspin",
        "Speed", "Stamina", "Tonicity", "Reflexes", "Strength",
        "Focus", "Tactic", "Positioning", "SelfEsteem", "DoubleSpirit",
    };

    private static readonly string[] Mentals = { "Concentration", "ColdBlood", "Constancy", "Motivation" };

    private static readonly string[] FieldOrder =
    {
        "Name", "Country", "BestRank", "Style", "Birthdate", "Body",
        "FirstYear", "StartRank", "RankPerYear", "SingleDouble",
        "Forehand_Power", "Forehand_Consistency", "Forehand_Precision",
        "Backhand_Power", "Backhand_Consistency", "Backhand_Precision",
        "Service_Power", "Service_Consistency", "Service_Precision",
        "Return", "Lob", "Passing", "Dropshot", "Counter",
        "ForehandVolley", "BackhandVolley", "Smash", "NetPresence", "Topspin",
        "Speed", "Stamina", "Tonicity", "Reflexes", "Strength",
        "Concentration", "Focus", "ColdBlood", "Constancy",
        "Tactic", "Positioning", "SelfEsteem", "Motivation", "DoubleSpirit", "Hand",
    };

    private static readonly string[] StyleTall = { "PowerBaseliner", "Puncher", "Volleyer", "Varied", "PowerBaseliner" };
    private static readonly string[] StyleShort = { "Defender", "Counter", "CounterPuncher", "AllRounder", "Defender" };
    private static readonly string[] StyleMid = { "PowerBaseliner", "Varied", "AllRounder", "Bulldog", "Puncher", "Counter" };

    private static string FmtLine(string key, object value)
    {
        int diff = 24 - key.Length;
        int tabs = Math.Max(1, (int)Math.Ceiling(diff / 8.0));
        return key + new string('\t', tabs) + "=\t" + value;
    }

    private static string ConvBirthdate(string iso)
    {
        if (string.IsNullOrEmpty(iso)) return "";
        if (iso.Length >= 10 &&
            DateTime.TryParseExact(iso[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            if (d.Year < 1940) return "";
            return d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }
        return "";
    }

    private static int MentalBase(int bestRank)
    {
        bestRank = Math.Max(1, Math.Min(bestRank, 1000));
        return (int)Math.Round(50 + 45 * (1 - (bestRank - 1) / 999.0), MidpointRounding.AwayFromZero);
    }

    private static int Clamp(double v, int lo = 1, int hi = 100)
        => Math.Max(lo, Math.Min(hi, (int)Math.Round(v, MidpointRounding.AwayFromZero)));

    private static Dictionary<string, int> DeriveSkills(DetRandom rng, string style, int bestRank)
    {
        var outv = new Dictionary<string, int>();
        StyleUp.TryGetValue(style, out var bias);
        foreach (var sk in Skills)
        {
            int b = bias != null && bias.TryGetValue(sk, out var bb) ? bb : 0;
            outv[sk] = Clamp(35 + b + rng.RandInt(-3, 3), 1, 70);
        }
        int baseM = MentalBase(bestRank);
        foreach (var m in Mentals)
        {
            int bump = m == "Motivation" ? 5 : 0;
            outv[m] = Clamp(baseM + bump + rng.RandInt(-4, 4), 40, 100);
        }
        return outv;
    }

    private static int EstimateWeight(int heightCm, string tour, DetRandom rng)
    {
        double bmi = tour == "WTA" ? 20.7 : 23.2;
        double w = bmi * Math.Pow(heightCm / 100.0, 2);
        return (int)Math.Round(w + rng.Uniform(-2, 3), MidpointRounding.AwayFromZero);
    }

    private static string DeriveStyle(DetRandom rng, int? height, string tour)
    {
        if (height is null or 0) return Styles[rng.RandRange(Styles.Length)];
        int tall = tour == "WTA" ? 180 : 190;
        int shortT = tour == "WTA" ? 168 : 180;
        string[] pool = height >= tall ? StyleTall : height <= shortT ? StyleShort : StyleMid;
        return pool[rng.RandRange(pool.Length)];
    }

    private static int CareerCeiling(int peak, int? age, Dictionary<int, int> ranks)
    {
        int a = age ?? 26;
        double f = a <= 19 ? 0.20 : a <= 21 ? 0.30 : a <= 23 ? 0.45 : a <= 25 ? 0.65
                 : a <= 27 ? 0.85 : a <= 29 ? 0.95 : 1.00;
        int lastYear = ranks.Keys.Max();
        bool rising = ranks[lastYear] <= peak * 1.1;
        if (rising && a <= 27) f *= 0.82;
        int ceiling = Math.Max(1, (int)Math.Round(peak * f, MidpointRounding.AwayFromZero));
        if (peak <= 3) ceiling = 1;
        return Math.Min(ceiling, peak);
    }

    /// <summary>Build the .ini text for the players ranked within [yearA, yearB].</summary>
    public static (string text, int count) Build(
        IReadOnlyDictionary<string, PlayerRecord> players, string tour, int yearA, int yearB)
    {
        var selected = players.Keys
            .Where(id => players[id].Ranks.Keys.Any(y => y >= yearA && y <= yearB))
            .ToList();

        selected.Sort((x, y) =>
        {
            var rx = players[x].Ranks; var ry = players[y].Ranks;
            int bx = rx.GetValueOrDefault(yearB, 10_000), by = ry.GetValueOrDefault(yearB, 10_000);
            if (bx != by) return bx.CompareTo(by);
            int ax = rx.GetValueOrDefault(yearA, 10_000), ay = ry.GetValueOrDefault(yearA, 10_000);
            if (ax != ay) return ax.CompareTo(ay);
            return string.CompareOrdinal(players[x].Name, players[y].Name);
        });

        var sb = new StringBuilder();
        sb.Append($"; {tour} Players.ini\n");
        sb.Append($"; Player set: ranked within {yearA}-{yearB}. RankPerYear is year-end history.\n");
        sb.Append($"; Ranks from official {tour} rankings; skills/style/build derived heuristically.\n\n");

        int idx = 0;
        foreach (var id in selected)
        {
            idx++;
            var rec = players[id];
            var ranks = rec.Ranks;
            int firstYear = ranks.Keys.Min();
            int bestYearEnd = ranks.Values.Min();
            int startRank = ranks[firstYear];

            int? age = null;
            if (!string.IsNullOrEmpty(rec.Dob) && rec.Dob.Length >= 4 &&
                int.TryParse(rec.Dob[..4], out var by))
                age = yearB - by;

            int peak = rec.CareerHigh.HasValue ? Math.Min(rec.CareerHigh.Value, bestYearEnd) : bestYearEnd;
            int ceiling = CareerCeiling(peak, age, ranks);

            var rpy = new List<string>();
            for (int yy = firstYear; yy <= yearB; yy++)
                rpy.Add(ranks.TryGetValue(yy, out var rk) ? rk.ToString() : "-2");
            string rankPerYear = string.Join(", ", rpy);

            var rng = new DetRandom(id);
            string style = DeriveStyle(rng, rec.Height, tour);
            var skills = DeriveSkills(rng, style, ceiling);

            string? hand = rec.Hand;
            if (string.IsNullOrEmpty(hand))
            {
                hand = rng.NextDouble() < 0.12 ? "Left" : "Right";
                if (rng.NextDouble() < 0.5) hand += " 2HBH";
            }

            int? h = rec.Height;
            int? w = rec.Weight;
            if (h.HasValue && !w.HasValue) w = EstimateWeight(h.Value, tour, rng);
            if (!h.HasValue)
            {
                if (tour == "WTA") { h = rng.RandInt(165, 185); w = rng.RandInt(57, 75); }
                else { h = rng.RandInt(175, 198); w = rng.RandInt(68, 92); }
            }
            string body = $"{h} {w}";

            string country = CountryCodes.Normalize(rec.Country);
            var fields = new Dictionary<string, object>
            {
                ["Name"] = string.IsNullOrEmpty(rec.Name) ? $"Player {id}" : rec.Name,
                ["Country"] = string.IsNullOrEmpty(country) ? "UNK" : country,
                ["BestRank"] = ceiling,
                ["Style"] = style,
                ["Birthdate"] = ConvBirthdate(rec.Dob),
                ["Body"] = body,
                ["FirstYear"] = firstYear,
                ["StartRank"] = startRank,
                ["RankPerYear"] = rankPerYear,
                ["SingleDouble"] = "-0.5",
                ["Concentration"] = skills["Concentration"],
                ["ColdBlood"] = skills["ColdBlood"],
                ["Constancy"] = skills["Constancy"],
                ["Motivation"] = skills["Motivation"],
                ["Hand"] = hand,
            };
            foreach (var sk in Skills) fields[sk] = skills[sk];

            sb.Append($"[Player{idx:D4}]\n");
            foreach (var key in FieldOrder)
                sb.Append(FmtLine(key, fields[key])).Append('\n');
            sb.Append('\n');
        }

        return (sb.ToString(), idx);
    }
}
