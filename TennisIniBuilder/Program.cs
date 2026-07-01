using TennisIniBuilder;

// ---- CLI ------------------------------------------------------------------
// TennisIniBuilder --from 2020 --to 2026 --tour both --out <dir>
//   [--no-bios] [--raw <file>] [--bios-file <file>]
//
// WTA: fetched live from the public JSON API + bios (WTA profiles + Wikipedia).
// ATP: the official endpoints are Cloudflare-protected and cannot be fetched from
//      a plain console app, so ATP builds from cached atp_raw.json / atp_bios.json
//      (produced via the browser-assisted harvest). Pass --raw to override.
// ---------------------------------------------------------------------------

var opts = ParseArgs(args);
if (opts is null) { PrintUsage(); return 1; }
var (from, to, tour, outDir, noBios, rawOverride, biosOverride) = opts.Value;
Directory.CreateDirectory(outDir);

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };

if (tour is "wta" or "both") await ProcessWtaAsync(from, to, outDir, noBios, rawOverride, biosOverride, http);
if (tour is "atp" or "both") ProcessAtp(from, to, outDir, rawOverride, biosOverride);
return 0;

// ---- WTA (live) -----------------------------------------------------------
static async Task ProcessWtaAsync(int from, int to, string outDir, bool noBios,
    string? rawOverride, string? biosOverride, HttpClient http)
{
    Console.WriteLine($"== WTA {from}-{to} ==");
    Dictionary<string, PlayerRecord> players;
    string? rawPath = rawOverride ?? FindExisting("wta_raw.json", outDir);
    bool live = rawPath is null;
    if (live)
    {
        Console.WriteLine("Fetching WTA rankings...");
        players = await new WtaClient(http).FetchRangeAsync(from, to);
    }
    else
    {
        Console.WriteLine($"Loading cached rankings: {rawPath}");
        players = DataLoader.LoadRaw(rawPath!);
    }
    if (players.Count == 0) { Console.WriteLine("No WTA players found."); return; }

    string? biosPath = biosOverride ?? FindExisting("wta_bios.json", outDir);
    if (biosPath is not null)
    {
        Console.WriteLine($"Merging cached bios: {biosPath}");
        DataLoader.MergeBios(players, DataLoader.LoadBios(biosPath));
    }
    else if (!noBios)
    {
        var enricher = new Enricher(http);
        var ordered = players.Values.OrderBy(RangeRank).ToList();
        Console.WriteLine("Enriching bios (WTA profiles)...");
        await enricher.EnrichFromWtaAsync(ordered);
        var missing = players.Values.Where(r => r.Height is null || r.Hand is null || r.CareerHigh is null)
                                    .OrderBy(RangeRank).ToList();
        Console.WriteLine($"Enriching remaining {missing.Count} via Wikipedia...");
        await enricher.EnrichFromWikipediaAsync(missing);
    }

    WriteIni(players, "WTA", from, to, Path.Combine(outDir, "wta_Players.ini"));

    static int RangeRank(PlayerRecord r) => r.Ranks.Count == 0 ? 100000 : r.Ranks.Values.Min();
}

// ---- ATP (cached; live needs a browser for Cloudflare) --------------------
static void ProcessAtp(int from, int to, string outDir, string? rawOverride, string? biosOverride)
{
    Console.WriteLine($"== ATP {from}-{to} ==");
    string? rawPath = rawOverride ?? FindExisting("atp_raw.json", outDir);
    if (rawPath is null)
    {
        Console.WriteLine("! ATP rankings/bios are Cloudflare-protected and cannot be fetched from a console app.");
        Console.WriteLine("  Provide cached data: place atp_raw.json (and atp_bios.json) in the output folder,");
        Console.WriteLine("  or pass --raw <atp_raw.json> [--bios-file <atp_bios.json>]. Skipping ATP.");
        return;
    }
    Console.WriteLine($"Loading cached rankings: {rawPath}");
    var players = DataLoader.LoadRaw(rawPath);
    string? biosPath = biosOverride ?? FindExisting("atp_bios.json", outDir);
    if (biosPath is not null)
    {
        Console.WriteLine($"Merging cached bios: {biosPath}");
        DataLoader.MergeBios(players, DataLoader.LoadBios(biosPath));
    }
    WriteIni(players, "ATP", from, to, Path.Combine(outDir, "atp_Players.ini"));
}

static void WriteIni(Dictionary<string, PlayerRecord> players, string tour, int from, int to, string path)
{
    var (text, count) = IniBuilder.Build(players, tour, from, to);
    File.WriteAllText(path, text);
    Console.WriteLine($"{tour}: wrote {count} players -> {path}");
}

static string? FindExisting(string name, string outDir)
{
    // Search the output/current dirs and a bundled `data/` folder (walking up from
    // the executable so cached ATP data under TennisIniBuilder/data is always found).
    var dirs = new List<string> { outDir, Directory.GetCurrentDirectory(), Path.Combine(outDir, "..") };
    var baseDir = AppContext.BaseDirectory;
    for (int up = 0; up <= 5; up++)
    {
        dirs.Add(baseDir);
        baseDir = Path.Combine(baseDir, "..");
    }
    foreach (var dir in dirs)
        foreach (var candidate in new[] { Path.Combine(dir, name), Path.Combine(dir, "data", name) })
        {
            var p = Path.GetFullPath(candidate);
            if (File.Exists(p)) return p;
        }
    return null;
}

static (int from, int to, string tour, string outDir, bool noBios, string? raw, string? bios)? ParseArgs(string[] a)
{
    int? from = null, to = null; string tour = "both", outDir = "."; bool noBios = false; string? raw = null, bios = null;
    for (int i = 0; i < a.Length; i++)
    {
        string k = a[i].ToLowerInvariant();
        string? Next() => i + 1 < a.Length ? a[++i] : null;
        switch (k)
        {
            case "--from": from = int.TryParse(Next(), out var f) ? f : null; break;
            case "--to": to = int.TryParse(Next(), out var t) ? t : null; break;
            case "--tour": tour = (Next() ?? "both").ToLowerInvariant(); break;
            case "--out": outDir = Next() ?? "."; break;
            case "--no-bios": noBios = true; break;
            case "--raw": raw = Next(); break;
            case "--bios-file": bios = Next(); break;
            default: Console.WriteLine($"Unknown arg: {a[i]}"); return null;
        }
    }
    if (from is null || to is null || from > to) return null;
    if (tour is not ("atp" or "wta" or "both")) return null;
    return (from.Value, to.Value, tour, outDir, noBios, raw, bios);
}

static void PrintUsage()
{
    Console.WriteLine("Usage: TennisIniBuilder --from <A> --to <B> [--tour atp|wta|both] [--out <dir>]");
    Console.WriteLine("                       [--no-bios] [--raw <rankings.json>] [--bios-file <bios.json>]");
    Console.WriteLine("Example: TennisIniBuilder --from 2020 --to 2026 --tour both --out .");
}
