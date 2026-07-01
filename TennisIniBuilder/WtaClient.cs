using System.Net.Http.Json;
using System.Text.Json;

namespace TennisIniBuilder;

/// <summary>Live WTA rankings via the public JSON API (api.wtatennis.com/tennis/players/ranked).</summary>
public sealed class WtaClient
{
    private const string Api = "https://api.wtatennis.com/tennis/players/ranked";
    private const int PageSize = 100;
    private const int Pages = 10; // 10 x 100 = top 1000
    private readonly HttpClient _http;

    public WtaClient(HttpClient http)
    {
        _http = http;
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://www.wtatennis.com");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.wtatennis.com/");
    }

    private static DateOnly LastMondayOfDecember(int year)
    {
        var d = new DateOnly(year, 12, 31);
        while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(-1);
        return d;
    }

    private async Task<List<JsonElement>> FetchRankedAsync(string at)
    {
        var rows = new List<JsonElement>();
        for (int page = 0; page < Pages; page++)
        {
            string url = $"{Api}?page={page}&pageSize={PageSize}&type=rankSingles&sort=asc&metric=SINGLES&name=&at={at}&nationality=";
            JsonElement data;
            try
            {
                data = await _http.GetFromJsonAsync<JsonElement>(url);
            }
            catch
            {
                break;
            }
            if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0) break;
            foreach (var e in data.EnumerateArray()) rows.Add(e.Clone());
            if (data.GetArrayLength() < PageSize) break;
            await Task.Delay(200);
        }
        return rows;
    }

    private async Task<(string? date, List<JsonElement> rows)> YearEndSnapshotAsync(int year, string? currentDate)
    {
        // The in-progress year has no year-end yet: use the current date.
        if (currentDate != null)
        {
            var rows0 = await FetchRankedAsync(currentDate);
            return (currentDate, rows0);
        }
        var baseDate = LastMondayOfDecember(year);
        for (int back = 0; back < 4; back++)
        {
            string cand = baseDate.AddDays(-7 * back).ToString("yyyy-MM-dd");
            var rows = await FetchRankedAsync(cand);
            if (rows.Count > 0) return (cand, rows);
        }
        return (null, new());
    }

    /// <summary>Fetch year-end snapshots for [yearA, yearB] and build player records.</summary>
    public async Task<Dictionary<string, PlayerRecord>> FetchRangeAsync(int yearA, int yearB)
    {
        var players = new Dictionary<string, PlayerRecord>();
        int thisYear = DateTime.UtcNow.Year;
        for (int year = yearA; year <= yearB; year++)
        {
            string? current = year >= thisYear ? DateTime.UtcNow.ToString("yyyy-MM-dd") : null;
            var (date, rows) = await YearEndSnapshotAsync(year, current);
            Console.WriteLine($"  {year}: {rows.Count,4} players (at {date ?? "n/a"})");
            foreach (var entry in rows)
            {
                if (!entry.TryGetProperty("player", out var p) || !p.TryGetProperty("id", out var idEl)) continue;
                string id = idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt64().ToString() : idEl.GetString() ?? "";
                if (id == "" || !entry.TryGetProperty("ranking", out var rEl) || rEl.ValueKind != JsonValueKind.Number) continue;
                int rank = rEl.GetInt32();
                if (!players.TryGetValue(id, out var rec))
                {
                    rec = new PlayerRecord
                    {
                        Id = id,
                        Name = (p.TryGetProperty("fullName", out var fn) ? fn.GetString() : "")?.Trim() ?? "",
                        Country = (p.TryGetProperty("countryCode", out var cc) ? cc.GetString() : "") ?? "",
                        Dob = (p.TryGetProperty("dateOfBirth", out var db) ? db.GetString() : "") ?? "",
                    };
                    players[id] = rec;
                }
                if (!rec.Ranks.TryGetValue(year, out var cur) || rank < cur) rec.Ranks[year] = rank;
            }
        }
        return players;
    }
}
