using AlabamaWalkabilityApi.Models;
using MySql.Data.MySqlClient;

namespace AlabamaWalkabilityApi.Services;

public class WalkabilityService : IWalkabilityService
{
    // Canonical list of US state FIPS codes (including DC), used for national views
    private static readonly Dictionary<string, string> StateFipsToAbbrev = new()
    {
        ["01"] = "AL", ["02"] = "AK", ["04"] = "AZ", ["05"] = "AR", ["06"] = "CA",
        ["08"] = "CO", ["09"] = "CT", ["10"] = "DE", ["11"] = "DC", ["12"] = "FL",
        ["13"] = "GA", ["15"] = "HI", ["16"] = "ID", ["17"] = "IL", ["18"] = "IN",
        ["19"] = "IA", ["20"] = "KS", ["21"] = "KY", ["22"] = "LA", ["23"] = "ME",
        ["24"] = "MD", ["25"] = "MA", ["26"] = "MI", ["27"] = "MN", ["28"] = "MS",
        ["29"] = "MO", ["30"] = "MT", ["31"] = "NE", ["32"] = "NV", ["33"] = "NH",
        ["34"] = "NJ", ["35"] = "NM", ["36"] = "NY", ["37"] = "NC", ["38"] = "ND",
        ["39"] = "OH", ["40"] = "OK", ["41"] = "OR", ["42"] = "PA", ["44"] = "RI",
        ["45"] = "SC", ["46"] = "SD", ["47"] = "TN", ["48"] = "TX", ["49"] = "UT",
        ["50"] = "VT", ["51"] = "VA", ["53"] = "WA", ["54"] = "WV", ["55"] = "WI", ["56"] = "WY"
    };

    private static readonly string[] AllStateFips =
    [
        "01", // AL
        "02", // AK
        "04", // AZ
        "05", // AR
        "06", // CA
        "08", // CO
        "09", // CT
        "10", // DE
        "11", // DC
        "12", // FL
        "13", // GA
        "15", // HI
        "16", // ID
        "17", // IL
        "18", // IN
        "19", // IA
        "20", // KS
        "21", // KY
        "22", // LA
        "23", // ME
        "24", // MD
        "25", // MA
        "26", // MI
        "27", // MN
        "28", // MS
        "29", // MO
        "30", // MT
        "31", // NE
        "32", // NV
        "33", // NH
        "34", // NJ
        "35", // NM
        "36", // NY
        "37", // NC
        "38", // ND
        "39", // OH
        "40", // OK
        "41", // OR
        "42", // PA
        "44", // RI
        "45", // SC
        "46", // SD
        "47", // TN
        "48", // TX
        "49", // UT
        "50", // VT
        "51", // VA
        "53", // WA
        "54", // WV
        "55", // WI
        "56"  // WY
    ];

    // Canonical Alabama county names, keyed by 3-digit county FIPS
    private static readonly Dictionary<string, string> AlabamaCountyNames = new()
    {
        ["001"] = "Autauga",
        ["003"] = "Baldwin",
        ["005"] = "Barbour",
        ["007"] = "Bibb",
        ["009"] = "Blount",
        ["011"] = "Bullock",
        ["013"] = "Butler",
        ["015"] = "Calhoun",
        ["017"] = "Chambers",
        ["019"] = "Cherokee",
        ["021"] = "Chilton",
        ["023"] = "Choctaw",
        ["025"] = "Clarke",
        ["027"] = "Clay",
        ["029"] = "Cleburne",
        ["031"] = "Coffee",
        ["033"] = "Colbert",
        ["035"] = "Conecuh",
        ["037"] = "Coosa",
        ["039"] = "Covington",
        ["041"] = "Crenshaw",
        ["043"] = "Cullman",
        ["045"] = "Dale",
        ["047"] = "Dallas",
        ["049"] = "DeKalb",
        ["051"] = "Elmore",
        ["053"] = "Escambia",
        ["055"] = "Etowah",
        ["057"] = "Fayette",
        ["059"] = "Franklin",
        ["061"] = "Geneva",
        ["063"] = "Greene",
        ["065"] = "Hale",
        ["067"] = "Henry",
        ["069"] = "Houston",
        ["071"] = "Jackson",
        ["073"] = "Jefferson",
        ["075"] = "Lamar",
        ["077"] = "Lauderdale",
        ["079"] = "Lawrence",
        ["081"] = "Lee",
        ["083"] = "Limestone",
        ["085"] = "Lowndes",
        ["087"] = "Macon",
        ["089"] = "Madison",
        ["091"] = "Marengo",
        ["093"] = "Marion",
        ["095"] = "Marshall",
        ["097"] = "Mobile",
        ["099"] = "Monroe",
        ["101"] = "Montgomery",
        ["103"] = "Morgan",
        ["105"] = "Perry",
        ["107"] = "Pickens",
        ["109"] = "Pike",
        ["111"] = "Randolph",
        ["113"] = "Russell",
        ["115"] = "St. Clair",
        ["117"] = "Shelby",
        ["119"] = "Sumter",
        ["121"] = "Talladega",
        ["123"] = "Tallapoosa",
        ["125"] = "Tuscaloosa",
        ["127"] = "Walker",
        ["129"] = "Washington",
        ["131"] = "Wilcox",
        ["133"] = "Winston"
    };

    private const string AlabamaStateFips = "01";

    private readonly WalkabilityDbContext _db;

    public WalkabilityService(WalkabilityDbContext db) => _db = db;

    private static string NormalizeCountyFips(string fips)
    {
        if (string.IsNullOrWhiteSpace(fips)) return "000";
        fips = fips.Trim();
        return fips.Length >= 3 ? fips : fips.PadLeft(3, '0');
    }

    private static string ResolveCountyName(string fips, string dbName)
    {
        var key = NormalizeCountyFips(fips);

        // If DB already has a non-generic name, use it
        if (!string.IsNullOrWhiteSpace(dbName) && !dbName.StartsWith("County ", StringComparison.OrdinalIgnoreCase))
            return dbName.Trim();

        // Otherwise fall back to canonical Alabama name when possible
        return AlabamaCountyNames.TryGetValue(key, out var canonical) ? canonical : dbName.Trim();
    }

    public async Task<PagedResult<CountyDto>> GetCountiesAsync(string? sort, int limit, CancellationToken ct = default)
    {
        var orderBy = sort?.ToLowerInvariant() == "walkabilityscore" ? "avg_walkability DESC" : "name ASC";
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);

        var countCmd = new MySqlCommand(
            "SELECT COUNT(*) FROM counties WHERE state_fips = @state", conn);
        countCmd.Parameters.AddWithValue("@state", AlabamaStateFips);
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        var cmd = new MySqlCommand($"""
            SELECT fips, name, avg_walkability, block_group_count, population
            FROM counties
            WHERE state_fips = @state
            ORDER BY {orderBy}
            LIMIT @limit
            """, conn);
        cmd.Parameters.AddWithValue("@state", AlabamaStateFips);
        cmd.Parameters.AddWithValue("@limit", limit);

        var items = new List<CountyDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var fips = r.IsDBNull(0) ? "" : (r.GetString(0) ?? "");
            var rawName = r.IsDBNull(1) ? null : r.GetString(1);
            var name = ResolveCountyName(fips, rawName);
            var avg = r.IsDBNull(2) ? 0.0 : r.GetDouble(2);
            var bgCount = r.IsDBNull(3) ? 0 : r.GetInt32(3);
            var pop = r.IsDBNull(4) ? 0 : r.GetInt32(4);
            items.Add(new CountyDto(fips, name, avg, bgCount, pop));
        }

        return new PagedResult<CountyDto>(total, items);
    }

    public async Task<StateStatsDto?> GetStateStatsAsync(CancellationToken ct = default)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);
        
        // Build WHERE clause to exclude territories (only include 50 states + DC)
        var stateFipsList = string.Join(",", AllStateFips.Select(f => $"'{f}'"));
        var whereClause = $"WHERE state_fips IN ({stateFipsList})";
        
        // National stats: aggregate across all states (excluding territories)
        var cmd = new MySqlCommand($"""
            SELECT AVG(walkability_score) avg_s, COUNT(*) cnt, SUM(population) pop
            FROM block_groups
            {whereClause}
            """, conn);
        
        double avg = 0;
        int cnt = 0;
        long pop = 0;
        
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (await r.ReadAsync(ct))
            {
                avg = r.IsDBNull(0) ? 0 : r.GetDouble(0);
                cnt = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                pop = r.IsDBNull(2) ? 0 : r.GetInt64(2);
            }
        }

        if (cnt == 0) return new StateStatsDto(0, 0, 0, 0, []);

        // Distribution query - national distribution (excluding territories)
        var distCmd = new MySqlCommand($"""
            SELECT FLOOR(walkability_score/5) AS bucket_key, COUNT(*) c
            FROM block_groups
            {whereClause}
            GROUP BY FLOOR(walkability_score/5)
            ORDER BY bucket_key
            """, conn);
        var buckets = new List<ScoreBucket>();
        await using (var dr = await distCmd.ExecuteReaderAsync(ct))
        {
            while (await dr.ReadAsync(ct))
            {
                var key = dr.IsDBNull(0) ? 0 : dr.GetInt32(0);
                var c = dr.IsDBNull(1) ? 0 : dr.GetInt32(1);
                buckets.Add(new ScoreBucket($"{key * 5}-{key * 5 + 5}", c));
            }
        }

        // Median query - national median across all states (excluding territories)
        var medCmd = new MySqlCommand($"""
            SELECT walkability_score FROM block_groups {whereClause} ORDER BY walkability_score LIMIT 1 OFFSET @off
            """, conn);
        medCmd.Parameters.AddWithValue("@off", cnt / 2);
        var median = 0.0;
        if (await medCmd.ExecuteScalarAsync(ct) is { } m) median = Convert.ToDouble(m);

        return new StateStatsDto(avg, median, cnt, (int)pop, buckets);
    }

    public async Task<IEnumerable<CountyDto>> GetCountyStatsAsync(string? sort, bool withDataOnly = false, CancellationToken ct = default)
    {
        var orderBy = sort?.ToLowerInvariant() == "walkabilityscore" ? "avg_walkability DESC" : "name ASC";
        var whereData = withDataOnly ? " AND (block_group_count > 0 OR population > 0)" : "";
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);
        var cmd = new MySqlCommand($"""
            SELECT fips, name, avg_walkability, block_group_count, population
            FROM counties WHERE state_fips = @state{whereData} ORDER BY {orderBy}
            """, conn);
        cmd.Parameters.AddWithValue("@state", AlabamaStateFips);
        var list = new List<CountyDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var fips = r.IsDBNull(0) ? "" : (r.GetString(0) ?? "");
            var rawName = r.IsDBNull(1) ? null : r.GetString(1);
            var name = ResolveCountyName(fips, rawName);
            var avg = r.IsDBNull(2) ? 0.0 : r.GetDouble(2);
            var bgCount = r.IsDBNull(3) ? 0 : r.GetInt32(3);
            var pop = r.IsDBNull(4) ? 0 : r.GetInt32(4);
            list.Add(new CountyDto(fips, name, avg, bgCount, pop));
        }
        return list;
    }

    public async Task<IEnumerable<ScoreBucket>> GetDistributionAsync(string? countyFips, CancellationToken ct = default)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);
        // Use only grouped expression in SELECT to satisfy ONLY_FULL_GROUP_BY; build bucket label in code
        var sql = """
            SELECT FLOOR(walkability_score/5) AS bucket_key, COUNT(*) c
            FROM block_groups
            """;
        if (!string.IsNullOrEmpty(countyFips))
        {
            sql += " WHERE state_fips = @state AND county_fips = @county";
        }
        sql += " GROUP BY FLOOR(walkability_score/5) ORDER BY bucket_key";

        var cmd = new MySqlCommand(sql, conn);
        if (!string.IsNullOrEmpty(countyFips))
        {
            cmd.Parameters.AddWithValue("@state", AlabamaStateFips);
            cmd.Parameters.AddWithValue("@county", countyFips);
        }

        var buckets = new List<ScoreBucket>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var key = r.IsDBNull(0) ? 0 : r.GetInt32(0);
            var c = r.IsDBNull(1) ? 0 : r.GetInt32(1);
            var bucket = $"{key * 5}-{key * 5 + 5}";
            buckets.Add(new ScoreBucket(bucket, c));
        }
        return buckets;
    }

    public async Task<IEnumerable<StateForecastDto>> GetStateForecastAsync(int years, CancellationToken ct = default)
    {
        if (years < 1) years = 1;
        if (years > 50) years = 50;

        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);

        // Per-state aggregates
        var cmd = new MySqlCommand("""
            SELECT state_fips, AVG(walkability_score) AS avg_s, COUNT(*) AS bg_count
            FROM block_groups
            GROUP BY state_fips
            """, conn);

        var states = new List<(string StateFips, double Avg, int Count)>();
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                var state = r.IsDBNull(0) ? "" : (r.GetString(0) ?? "");
                var avg = r.IsDBNull(1) ? 0.0 : r.GetDouble(1);
                var cnt = r.IsDBNull(2) ? 0 : r.GetInt32(2);
                if (!string.IsNullOrWhiteSpace(state) && cnt > 0)
                    states.Add((state.Trim(), avg, cnt));
            }
        }

        // If we somehow have no data at all, still return a zeroed-out entry for each state
        if (states.Count == 0)
        {
            return AllStateFips
                .Select(fips => new StateForecastDto(fips, 0.0, 0.0, 0))
                .ToArray();
        }

        // Weighted national mean
        var totalBg = states.Sum(s => (long)s.Count);
        var nationalMean = totalBg > 0
            ? states.Sum(s => s.Avg * s.Count) / totalBg
            : states.Average(s => s.Avg);

        // Heuristic: over N years, each state's avg drifts partway toward the national mean.
        // This is a speculative model, not a real forecast.
        var factor = Math.Clamp(years / 20.0, 0.0, 1.0); // ~20-year half-life toward mean

        var result = new List<StateForecastDto>();
        foreach (var s in states)
        {
            var current = s.Avg;
            var drift = (nationalMean - current) * 0.5 * factor; // move half-way over 20 years
            var predicted = current + drift;
            result.Add(new StateForecastDto(s.StateFips, current, predicted, s.Count));
        }

        // Ensure every state appears at least once, even if there was no data in block_groups
        var existing = new HashSet<string>(result.Select(r => r.StateFips));
        foreach (var fips in AllStateFips)
        {
            if (!existing.Contains(fips))
            {
                result.Add(new StateForecastDto(fips, 0.0, 0.0, 0));
            }
        }

        return result;
    }

    public async Task<IEnumerable<StateRecommendationDto>> GetStateRecommendationsAsync(CancellationToken ct = default)
    {
        var forecast = await GetStateForecastAsync(1, ct);
        var list = new List<StateRecommendationDto>();

        foreach (var s in forecast.Where(x => x.BlockGroupCount > 0))
        {
            var score = s.CurrentAvgWalkability;
            var abbrev = StateFipsToAbbrev.GetValueOrDefault(s.StateFips, s.StateFips);
            var (priority, recommendation) = GetRecommendation(score);
            list.Add(new StateRecommendationDto(s.StateFips, abbrev, score, priority, recommendation));
        }

        return list
            .OrderBy(x => x.CurrentScore)
            .Take(15)
            .ToList();
    }

    private static (string Priority, string Recommendation) GetRecommendation(double score)
    {
        if (score < 5)
            return ("High", "Prioritize transit expansion, mixed-use development, and pedestrian infrastructure.");
        if (score < 10)
            return ("Moderate", "Focus on intersection density, transit proximity, and land-use diversity.");
        if (score < 15)
            return ("Maintain", "Continue current policies; consider incremental improvements.");
        return ("Exemplary", "Share best practices with other states.");
    }
}
