using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TennisIniBuilder;

/// <summary>
/// Enriches player records with real height / hand / career-high. WTA profile pages
/// first (sequential — the site strips pages under concurrency), then Wikipedia
/// (batched, name-based) for whatever is still missing.
/// </summary>
public sealed partial class Enricher
{
    private readonly HttpClient _http;
    public Enricher(HttpClient http) { _http = http; }

    [GeneratedRegex(@"profile-bio__info-title"">(.*?)</h2>\s*<span[^>]*profile-bio__info-content[^>]*>(.*?)</span>", RegexOptions.Singleline)]
    private static partial Regex BioPair();
    [GeneratedRegex(@"\((\d\.\d{2})m\)")]
    private static partial Regex HeightMeters();

    /// <summary>WTA profile scrape (sequential). Fills Height/Hand/CareerHigh where published.</summary>
    public async Task EnrichFromWtaAsync(IReadOnlyList<PlayerRecord> ordered)
    {
        int ok = 0, i = 0;
        foreach (var rec in ordered)
        {
            i++;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"https://www.wtatennis.com/players/{rec.Id}/x");
                req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                req.Headers.TryAddWithoutValidation("Accept", "text/html");
                var resp = await _http.SendAsync(req);
                if (resp.StatusCode != HttpStatusCode.OK) continue;
                string html = await resp.Content.ReadAsStringAsync();
                bool got = false;
                foreach (Match m in BioPair().Matches(html))
                {
                    string key = StripTags(m.Groups[1].Value).Trim().ToLowerInvariant();
                    string val = StripTags(m.Groups[2].Value).Trim();
                    if (key.Contains("height"))
                    {
                        var hm = HeightMeters().Match(val);
                        if (hm.Success) { rec.Height = (int)Math.Round(double.Parse(hm.Groups[1].Value) * 100); got = true; }
                    }
                    else if (key.Contains("plays") && rec.Hand is null)
                    {
                        rec.Hand = val.ToLowerInvariant().Contains("left") ? "Left" : "Right";
                        got = true;
                    }
                    else if (key.Contains("career high"))
                    {
                        var cm = Regex.Match(val, @"\d+");
                        if (cm.Success) { rec.CareerHigh = int.Parse(cm.Value); got = true; }
                    }
                }
                if (got) ok++;
            }
            catch { /* leave for Wikipedia / estimate */ }
            if (i % 100 == 0) Console.WriteLine($"    WTA bios {i}/{ordered.Count} ok={ok}");
        }
        Console.WriteLine($"  WTA profile bios: {ok}/{ordered.Count}");
    }

    /// <summary>Wikipedia infobox fallback (batched, name-based) for players still missing data.</summary>
    public async Task EnrichFromWikipediaAsync(IReadOnlyList<PlayerRecord> missing)
    {
        const int batch = 40;
        int added = 0;
        for (int i = 0; i < missing.Count; i += batch)
        {
            var chunk = missing.Skip(i).Take(batch).ToList();
            string titles = string.Join("|", chunk.Select(r => r.Name));
            string url = "https://en.wikipedia.org/w/api.php?action=query&prop=revisions&rvprop=content&rvslots=main" +
                         "&format=json&redirects=1&titles=" + Uri.EscapeDataString(titles);
            JsonDocument? doc = null;
            for (int attempt = 0; attempt < 4 && doc is null; attempt++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.TryAddWithoutValidation("User-Agent", "tennis-ini-builder/1.0 (hobby project)");
                    var resp = await _http.SendAsync(req);
                    doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                }
                catch { await Task.Delay(1500 * (attempt + 1)); }
            }
            if (doc is null) continue;
            using (doc)
            {
                var (resolve, byTitle) = BuildTitleMaps(doc.RootElement);
                foreach (var rec in chunk)
                {
                    if (!byTitle.TryGetValue(resolve(rec.Name), out var txt)) continue;
                    if (!txt.ToLowerInvariant().Contains("plays")) continue; // require tennis infobox
                    if (rec.Height is null) { var h = ParseWikiHeight(txt); if (h is > 0) rec.Height = h; }
                    if (rec.Hand is null) { var hd = ParseWikiPlays(txt); if (hd != null) rec.Hand = hd; }
                    if (rec.CareerHigh is null) { var ch = ParseWikiCareerHigh(txt); if (ch is > 0) rec.CareerHigh = ch; }
                    added++;
                }
            }
            Console.WriteLine($"    Wikipedia {Math.Min(i + batch, missing.Count)}/{missing.Count} touched={added}");
            await Task.Delay(600);
        }
        Console.WriteLine($"  Wikipedia fallback touched {added} players");
    }

    private static (Func<string, string> resolve, Dictionary<string, string> byTitle) BuildTitleMaps(JsonElement root)
    {
        var step = new Dictionary<string, string>();
        if (root.TryGetProperty("query", out var q))
        {
            foreach (var key in new[] { "normalized", "redirects" })
                if (q.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var e in arr.EnumerateArray())
                        step[e.GetProperty("from").GetString()!] = e.GetProperty("to").GetString()!;
        }
        string Resolve(string t)
        {
            var seen = new HashSet<string>();
            while (step.TryGetValue(t, out var nxt) && seen.Add(t)) t = nxt;
            return t;
        }
        var byTitle = new Dictionary<string, string>();
        if (q.ValueKind == JsonValueKind.Object && q.TryGetProperty("pages", out var pages))
            foreach (var pg in pages.EnumerateObject())
            {
                var v = pg.Value;
                if (v.TryGetProperty("title", out var t) && v.TryGetProperty("revisions", out var revs) &&
                    revs.ValueKind == JsonValueKind.Array && revs.GetArrayLength() > 0)
                {
                    var content = revs[0].GetProperty("slots").GetProperty("main").GetProperty("*").GetString();
                    if (content != null) byTitle[t.GetString()!] = content;
                }
            }
        return (Resolve, byTitle);
    }

    private static int? ParseWikiHeight(string txt)
    {
        var m = Regex.Match(txt, @"\|\s*height\s*=\s*([^\n]*)", RegexOptions.IgnoreCase);
        if (!m.Success || string.IsNullOrWhiteSpace(m.Groups[1].Value)) return null;
        string v = m.Groups[1].Value.Trim();
        var hm = Regex.Match(v, @"\{\{\s*height\s*\|([^}]*)\}\}", RegexOptions.IgnoreCase);
        if (hm.Success)
        {
            var mm = Regex.Match(hm.Groups[1].Value, @"m\s*=\s*([\d.]+)");
            if (mm.Success) return (int)Math.Round(double.Parse(mm.Groups[1].Value) * 100);
            var ft = Regex.Match(hm.Groups[1].Value, @"ft\s*=\s*(\d+)");
            if (ft.Success)
            {
                var inch = Regex.Match(hm.Groups[1].Value, @"in\s*=\s*(\d+)");
                return (int)Math.Round((int.Parse(ft.Groups[1].Value) * 12 + (inch.Success ? int.Parse(inch.Groups[1].Value) : 0)) * 2.54);
            }
        }
        var cm = Regex.Match(v, @"\{\{\s*convert\s*\|\s*([\d.]+)\s*\|\s*m\b", RegexOptions.IgnoreCase);
        if (cm.Success) return (int)Math.Round(double.Parse(cm.Groups[1].Value) * 100);
        var cc = Regex.Match(v, @"\{\{\s*convert\s*\|\s*(\d+)\s*\|\s*cm\b", RegexOptions.IgnoreCase);
        if (cc.Success) return int.Parse(cc.Groups[1].Value);
        var pm = Regex.Match(v, @"(\d\.\d{1,2})\s*m\b");
        if (pm.Success) return (int)Math.Round(double.Parse(pm.Groups[1].Value) * 100);
        var pc = Regex.Match(v, @"(\d{3})\s*cm");
        if (pc.Success) return int.Parse(pc.Groups[1].Value);
        var fi = Regex.Match(v, @"(\d+)\s*ft\s*(\d+)?\s*in");
        if (fi.Success) return (int)Math.Round((int.Parse(fi.Groups[1].Value) * 12 + (fi.Groups[2].Success ? int.Parse(fi.Groups[2].Value) : 0)) * 2.54);
        return null;
    }

    private static string? ParseWikiPlays(string txt)
    {
        var m = Regex.Match(txt, @"\|\s*plays\s*=\s*([^\n]*)", RegexOptions.IgnoreCase);
        if (!m.Success || string.IsNullOrWhiteSpace(m.Groups[1].Value)) return null;
        string v = m.Groups[1].Value.ToLowerInvariant();
        string hand = v.Contains("left") ? "Left" : "Right";
        if (v.Contains("two-handed") || v.Contains("two handed")) hand += " 2HBH";
        return hand;
    }

    private static int? ParseWikiCareerHigh(string txt)
    {
        var m = Regex.Match(txt, @"\|\s*highestsinglesranking\s*=\s*(?:No\.?\s*)?(\d+)", RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    private static string StripTags(string s) => Regex.Replace(s, "<[^>]+>", " ");
}
