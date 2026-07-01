using System.Text.Json;

namespace TennisIniBuilder;

/// <summary>Loads players from cached raw rankings JSON (either shape) and merges bio sidecars.</summary>
public static class DataLoader
{
    public static Dictionary<string, PlayerRecord> LoadRaw(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        // ATP shape: { "players": { "<id>": { name, ranks:{year:rank} } } }
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("players", out var pl) && pl.ValueKind == JsonValueKind.Object)
            return ParsePlayerKeyed(pl);

        // Year-keyed (WTA): { "<year>": { "rows": [ {player:{...}, ranking} ] } }
        bool yearKeyed = root.EnumerateObject().Any(p =>
            int.TryParse(p.Name, out _) && p.Value.ValueKind == JsonValueKind.Object &&
            p.Value.TryGetProperty("rows", out _));
        if (yearKeyed) return ParseYearKeyed(root);

        return ParsePlayerKeyed(root);
    }

    private static Dictionary<string, PlayerRecord> ParseYearKeyed(JsonElement root)
    {
        var players = new Dictionary<string, PlayerRecord>();
        foreach (var yearProp in root.EnumerateObject())
        {
            if (!int.TryParse(yearProp.Name, out int year)) continue;
            if (!yearProp.Value.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array) continue;
            foreach (var entry in rows.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                var p = entry.TryGetProperty("player", out var pp) && pp.ValueKind == JsonValueKind.Object ? pp : entry;
                if (p.ValueKind != JsonValueKind.Object || !p.TryGetProperty("id", out var idEl)) continue;
                if (idEl.ValueKind is not (JsonValueKind.Number or JsonValueKind.String)) continue;
                string id = idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt64().ToString() : idEl.GetString() ?? "";
                if (id == "") continue;
                int? rank = entry.TryGetProperty("ranking", out var rEl) && rEl.ValueKind == JsonValueKind.Number
                    ? rEl.GetInt32()
                    : entry.TryGetProperty("rank", out var r2) && r2.ValueKind == JsonValueKind.Number ? r2.GetInt32() : null;
                if (rank is null) continue;

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
                if (!rec.Ranks.TryGetValue(year, out var cur) || rank.Value < cur)
                    rec.Ranks[year] = rank.Value;
            }
        }
        return players;
    }

    private static Dictionary<string, PlayerRecord> ParsePlayerKeyed(JsonElement obj)
    {
        var players = new Dictionary<string, PlayerRecord>();
        foreach (var prop in obj.EnumerateObject())
        {
            var rec = prop.Value;
            if (rec.ValueKind != JsonValueKind.Object) continue;
            var ranks = new Dictionary<int, int>();
            if (rec.TryGetProperty("ranks", out var rk) && rk.ValueKind == JsonValueKind.Object)
                foreach (var yr in rk.EnumerateObject())
                    if (int.TryParse(yr.Name, out int y) && yr.Value.ValueKind == JsonValueKind.Number)
                        ranks[y] = yr.Value.GetInt32();
            if (ranks.Count == 0) continue;

            string name = (rec.TryGetProperty("name", out var nm) ? nm.GetString()
                        : rec.TryGetProperty("fullName", out var fn) ? fn.GetString() : "")?.Trim() ?? "";
            string country = (rec.TryGetProperty("countryCode", out var cc) ? cc.GetString()
                           : rec.TryGetProperty("country", out var c2) ? c2.GetString() : "") ?? "";
            string dob = (rec.TryGetProperty("dateOfBirth", out var db) ? db.GetString()
                       : rec.TryGetProperty("dob", out var d2) ? d2.GetString() : "") ?? "";

            players[prop.Name] = new PlayerRecord { Id = prop.Name, Name = name, Country = country, Dob = dob, Ranks = ranks };
        }
        return players;
    }

    public static Dictionary<string, Bio> LoadBios(string path)
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<Dictionary<string, Bio>>(File.ReadAllText(path), opts) ?? new();
    }

    /// <summary>Overlay real bio fields onto the player records (id key-type agnostic).</summary>
    public static void MergeBios(Dictionary<string, PlayerRecord> players, Dictionary<string, Bio> bios)
    {
        foreach (var (pid, b) in bios)
        {
            if (!players.TryGetValue(pid, out var rec)) continue;
            if (!string.IsNullOrEmpty(b.Country)) rec.Country = b.Country!;
            if (!string.IsNullOrEmpty(b.Dob)) rec.Dob = b.Dob!;
            string ph = (b.PlayHand ?? "").ToUpperInvariant();
            if (ph is "L" or "R")
            {
                string hand = ph == "L" ? "Left" : "Right";
                if (b.Backhand == "2") hand += " 2HBH";
                rec.Hand = hand;
            }
            if (b.Height is > 0) rec.Height = b.Height;
            if (b.Weight is > 0) rec.Weight = b.Weight;
            if (b.CareerHigh is > 0) rec.CareerHigh = b.CareerHigh;
        }
    }
}
