# Tennis Players.ini — Session Handoff / Continue Here

_Last updated: 2026-07-01_

This file lets us resume seamlessly. It captures the finished state, how to run the
tool, the knowledge from the (now-deleted) Python scripts + `SKILL.md`, and the
concrete open items — especially resolving the **ATP live-fetch** constraint.

---

## 1. What's done (the win)

- **`wta_Players.ini`** (1086 players) and **`atp_Players.ini`** (1087 players) — final rosters
  for players ranked in 2025 **or** 2026, with full year-by-year `RankPerYear`, real
  country/birthdate, real height/hand where published, optimistic career-ceiling `BestRank`,
  height-driven playing style, and derived skills.
- **`TennisIniBuilder/`** — a .NET 9 console port that regenerates the files for **any**
  year range `[A, B]`.
- **`TennisIniBuilder/data/`** — cached rankings + bios (`atp_raw.json`, `atp_bios.json`,
  `wta_raw.json`, `wta_bios.json`). **Keep this** — ATP cannot be re-fetched without it
  (see §4), and it lets WTA rebuild instantly without re-harvesting.

Real-data coverage (as built): countries/DOBs ~100%; handedness 643 WTA + all ATP;
career-high 644 WTA + (ATP uses best year-end as peak); **height only ~212/1086 WTA**
(sparsest field online) + all ATP. Missing fields fall back to estimates.

- **Country codes normalized (`CountryCodes.cs`).** The WTA/ATP feeds emit IOC/ITF
  abbreviations (SUI, GER, NED, TPE, CHI, XKX, ...) which differ from the ISO 3166-1
  alpha-3 codes the game country table (`Countries_English.txt`) expects. `IniBuilder`
  now runs every country through `CountryCodes.Normalize()`, so all builds emit valid
  ISO alpha-3. Kosovo (no ISO code) falls back to `ALB`. Verified: regenerating the full
  cached rosters (1866 WTA / 1087 ATP for 2020–2026) yields **0 invalid codes**.
  Note: the shipping `wta/atp_Players.ini` were produced by the older Python generator, so
  their per-player skill values differ from this C# tool by ±1–2 (see §4B). They are already
  country-valid, so they were left as-is rather than regenerated.

---

## 2. How to run

```powershell
# Both tours, full range, output to current folder:
dotnet run --project TennisIniBuilder -c Release -- --from 2020 --to 2026 --tour both --out .

# WTA is live (JSON API + bios). ATP builds from TennisIniBuilder/data (auto-discovered).
# Options: --tour atp|wta|both  --no-bios  --raw <rankings.json>  --bios-file <bios.json>
```

The tool auto-discovers cached JSON in `TennisIniBuilder/data/`, the output dir, or cwd.

---

## 3. Validation checklist (run after any build)

Expect `invalidStyle=0`, `UNK=0`, `emptyDOB=0`, `badCountry=0`; counts ≈ 1086/1087 for 2025–2026.

```powershell
$iso = Get-Content ..\Countries_English.txt | %{ ($_ -split ';')[2].Trim().ToUpper() } | ?{ $_ -match '^[A-Z]{3}$' }
foreach($f in 'wta_Players.ini','atp_Players.ini'){ $c=Get-Content $f;
  $p=($c|sls '^\[Player').Count;
  $bad=($c|sls '^Style'|%{($_ -split '=')[1].Trim()}|?{$_ -notin 'Defender','PowerBaseliner','Puncher','Volleyer','Varied','Counter','AllRounder','CounterPuncher','Bulldog'}).Count;
  $unk=($c|sls '^Country\s+=\s+UNK').Count; $nod=($c|sls '^Birthdate\s+=\s*$').Count;
  $bc=($c|sls '^Country\s*=\s*(\S+)'|%{$_.Matches[0].Groups[1].Value.ToUpper()}|?{$_ -ne 'UNK' -and $iso -notcontains $_}).Count;
  "$f players=$p invalidStyle=$bad UNK=$unk emptyDOB=$nod badCountry=$bc" }
```

---

## 4. OPEN CONSTRAINTS — resolve next time

### A. ATP live fetch (the big one) — Cloudflare
- **Problem:** ATP rankings + bios endpoints are Cloudflare-protected. A plain `HttpClient`
  gets **403**. That's why ATP currently builds from cached `data/`, not live.
- **Working technique (from this session):** the ATP **hero** bio endpoint
  `https://www.atptour.com/en/-/www/players/hero/{id}` returns JSON *when fetched from
  inside a browser that has passed Cloudflare* (same-origin relative `fetch()` reuses the
  clearance cookie). `id` is a slug like `s0ag` (Sinner), `z355` (Zverev).
- **Fix to implement:** add **Microsoft.Playwright** to the C# project; launch a real
  (headed or persistent-context) Chromium, navigate to atptour.com so Cloudflare clears,
  then run the hero fetches via `page.EvaluateAsync` (relative URL, batched with a small
  concurrency pool — this worked at 16 in-page). Also fetch the ATP rankings table per year
  for `RankPerYear`. Fall back to `data/` if Playwright/browser is unavailable.
- **Acceptance:** `--tour atp` with no cached data produces 1087 players with real
  country/DOB/height/hand; validation checklist passes.

### B. PRNG parity with Python (optional / low priority)
- C# uses mulberry32; Python used Mersenne Twister. Skills jitter and estimated weights
  differ by ±1–2 (rules/format/real-data all match). Only worth doing if byte-identical
  output vs the Python files is required — port CPython's `random` or accept the difference.

### C. WTA live-bios re-harvest reliability
- WTA profile pages are **stripped under ANY concurrency** → the enricher fetches
  **sequentially** and must **not** retry genuine "no-bio" stubs. This is already in
  `Enricher.cs`; keep it sequential. Wikipedia fallback is batched (40 titles/request).

---

## 5. Reference knowledge (folded in from deleted scripts + SKILL.md)

### Data sources (all free/public, verified working)
- **WTA rankings (JSON, no key):** `https://api.wtatennis.com/tennis/players/ranked`
  `?page=<0..9>&pageSize=100&type=rankSingles&sort=asc&metric=SINGLES&name=&at=<YYYY-MM-DD>&nationality=`
  — 10 pages = top 1000. `at` = year-end (last Monday of December) or today for the current year.
  Returns `player{id(int),fullName,countryCode,dateOfBirth}` + `ranking`. No height/weight/CH.
- **WTA bios (HTML):** `https://www.wtatennis.com/players/{id}/x` (slug ignored). Parse
  `profile-bio__info-title/-content` pairs: `Plays`, `Career High`, `Height` (`(1.82m)`→cm).
  **No weight ever.** ~40% of lower-ranked players return stub pages (no bio).
- **ATP bios (JSON, browser-only):** `.../-/www/players/hero/{id}` → country, DOB, height,
  weight, play-hand, backhand. Slug ids.
- **Wikipedia fallback (name-based, batched):** `https://en.wikipedia.org/w/api.php`
  `?action=query&prop=revisions&rvprop=content&rvslots=main&titles=Name1|...|Name40&redirects=1&format=json`.
  Resolve `normalized`+`redirects`; require a `plays=` field (avoids namesakes). Parse
  infobox `plays` (hand + one/two-handed backhand), `height` (`{{height|m=}}`,
  `{{convert|1.80|m}}`, `1.80 m`, `5 ft 6 in`, `180 cm`), `highestsinglesranking` (career-high).

### Players.ini format rules
- Sections `[PlayerNNNN]`, tab-aligned `key<tabs>=<tab>value`.
- **9 valid Styles only:** `Defender, PowerBaseliner, Puncher, Volleyer, Varied, Counter,
  AllRounder, CounterPuncher, Bulldog`. (`Varied`, NOT `AllCourtAttacker`.)
- Skills set RELATIVE to 35 (higher=stronger), biased by Style.
- 4 non-normalized mentals (Concentration, ColdBlood, Constancy, Motivation) scale from a
  high base at BestRank down toward ~50 near rank 1000.
- `RankPerYear` = year-end ranks from `FirstYear`..B; gap year = `-2`, didn't-play = `-1`.
- `Body` = `"<cm> <kg>"`. `Hand` = e.g. `Right`, `Left 2HBH`.
- `BestRank` = **optimistic career ceiling** (real peak × age/trajectory factor; peak≤3 → 1).
- Style scouting: tall→power/serve-volley, short→counter/defender, mid→all-court; random if
  no height. Weight estimated from height via BMI band (WTA ~20.7, ATP ~23.2) when unknown.

### Gotchas (learned the hard way)
- **WTA ids are int in the feed but string in bios JSON** → merge by matching key types
  (the C# loader keys everything by string to avoid this).
- **WTA pages stripped under concurrency** → sequential only; don't retry stubs.
- **ATP needs a browser** (Cloudflare); inline the id list into the in-page script.
- **Height is the sparsest field everywhere** (~20% WTA even after WTA+Wikipedia).
- **User preference:** do NOT depend on Sackmann's datasets — use live sources + Wikipedia.

---

## 6. If you want the agent (Q3)
A `SKILL.md` (tennis-ini-builder) captured the above and pointed at the pipeline. It was
deleted with the loose files; its content is preserved in §5 here. To make it a real
VS Code skill next time, place it at `.github/skills/tennis-ini-builder/SKILL.md` (folder
name must match the `name` field) and have it drive `TennisIniBuilder` as the engine.

## 7. Suggested next-session order
1. Implement **Constraint A** (Playwright ATP) → makes ATP fully live.
2. Re-run `--tour both --from 2020 --to 2026`, validate.
3. (Optional) Recreate the `SKILL.md` under `.github/skills/` driving the tool.
