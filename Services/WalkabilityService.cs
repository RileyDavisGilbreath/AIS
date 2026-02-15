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

    private readonly WalkabilityDbContext _db;

    public WalkabilityService(WalkabilityDbContext db) => _db = db;

    private static string NormalizeCountyFips(string fips)
    {
        if (string.IsNullOrWhiteSpace(fips)) return "000";
        fips = fips.Trim();
        return fips.Length >= 3 ? fips : fips.PadLeft(3, '0');
    }

    private static string ResolveCountyName(string fips, string? dbName)
    {
        if (!string.IsNullOrWhiteSpace(dbName) && !dbName.StartsWith("County ", StringComparison.OrdinalIgnoreCase))
            return dbName.Trim();
        return string.IsNullOrWhiteSpace(dbName) ? "County " + NormalizeCountyFips(fips) : dbName.Trim();
    }

    public async Task<PagedResult<CountyDto>> GetCountiesAsync(string? stateFips, string? sort, int limit, CancellationToken ct = default)
    {
        var orderBy = sort?.ToLowerInvariant() == "walkabilityscore" ? "avg_walkability DESC" : "name ASC";
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);

        var countSql = string.IsNullOrEmpty(stateFips)
            ? "SELECT COUNT(*) FROM counties"
            : "SELECT COUNT(*) FROM counties WHERE state_fips = @state";
        var countCmd = new MySqlCommand(countSql, conn);
        if (!string.IsNullOrEmpty(stateFips))
            countCmd.Parameters.AddWithValue("@state", stateFips.Trim().Length == 1 ? "0" + stateFips.Trim() : stateFips.Trim());
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        var whereClause = string.IsNullOrEmpty(stateFips) ? "" : " WHERE state_fips = @state";
        var cmd = new MySqlCommand($"""
            SELECT fips, name, avg_walkability, block_group_count, population
            FROM counties{whereClause}
            ORDER BY {orderBy}
            LIMIT @limit
            """, conn);
        if (!string.IsNullOrEmpty(stateFips))
            cmd.Parameters.AddWithValue("@state", stateFips.Trim().Length == 1 ? "0" + stateFips.Trim() : stateFips.Trim());
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

    public async Task<IEnumerable<CountyDto>> GetCountyStatsAsync(string? stateFips, string? sort, bool withDataOnly = false, CancellationToken ct = default)
    {
        var orderBy = sort?.ToLowerInvariant() == "walkabilityscore" ? "avg_walkability DESC" : "name ASC";
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(stateFips)) parts.Add("state_fips = @state");
        if (withDataOnly) parts.Add("(block_group_count > 0 OR population > 0)");
        var whereClause = parts.Count == 0 ? "" : " WHERE " + string.Join(" AND ", parts);
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);
        var cmd = new MySqlCommand($"""
            SELECT fips, name, avg_walkability, block_group_count, population
            FROM counties{whereClause} ORDER BY {orderBy}
            """, conn);
        if (!string.IsNullOrEmpty(stateFips))
            cmd.Parameters.AddWithValue("@state", stateFips.Trim().Length == 1 ? "0" + stateFips.Trim() : stateFips.Trim());
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

    public async Task<IEnumerable<ScoreBucket>> GetDistributionAsync(string? stateFips, string? countyFips, CancellationToken ct = default)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);
        var sql = """
            SELECT FLOOR(walkability_score/5) AS bucket_key, COUNT(*) c
            FROM block_groups
            """;
        if (!string.IsNullOrEmpty(stateFips)) sql += " WHERE state_fips = @state";
        if (!string.IsNullOrEmpty(countyFips)) sql += (string.IsNullOrEmpty(stateFips) ? " WHERE " : " AND ") + "county_fips = @county";
        sql += " GROUP BY FLOOR(walkability_score/5) ORDER BY bucket_key";

        var cmd = new MySqlCommand(sql, conn);
        if (!string.IsNullOrEmpty(stateFips))
            cmd.Parameters.AddWithValue("@state", stateFips.Trim().Length == 1 ? "0" + stateFips.Trim() : stateFips.Trim());
        if (!string.IsNullOrEmpty(countyFips))
            cmd.Parameters.AddWithValue("@county", countyFips.Trim().Length < 3 ? countyFips.Trim().PadLeft(3, '0') : countyFips.Trim());

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

        // Current state aggregates from block_groups
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

        if (states.Count == 0)
        {
            return AllStateFips
                .Select(fips => new StateForecastDto(fips, 0.0, 0.0, 0))
                .ToArray();
        }

        // Load 2010 state averages for trend-based forecast (table may not exist or be empty)
        var avg2010ByState = new Dictionary<string, double>();
        try
        {
            var cmd2010 = new MySqlCommand("""
                SELECT state_fips, AVG(walkability_score) AS avg_s
                FROM block_groups_2010
                GROUP BY state_fips
                """, conn);
            await using (var r = await cmd2010.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                {
                    var sf = r.IsDBNull(0) ? "" : (r.GetString(0) ?? "").Trim();
                    var a = r.IsDBNull(1) ? 0.0 : r.GetDouble(1);
                    if (!string.IsNullOrEmpty(sf))
                        avg2010ByState[sf] = a;
                }
            }
        }
        catch
        {
            // block_groups_2010 may not exist or be empty
        }

        const int baseYear = 2010;
        var currentYear = DateTime.UtcNow.Year;
        var yearsBaseToCurrent = Math.Max(1, currentYear - baseYear);

        var totalBg = states.Sum(s => (long)s.Count);
        var nationalMean = totalBg > 0
            ? states.Sum(s => s.Avg * s.Count) / totalBg
            : states.Average(s => s.Avg);
        var factor = Math.Clamp(years / 20.0, 0.0, 1.0);

        var result = new List<StateForecastDto>();
        foreach (var s in states)
        {
            var current = s.Avg;
            double predicted;

            if (avg2010ByState.TryGetValue(s.StateFips, out var avg2010))
            {
                // Trend-based: full linear extrapolation from 2010 -> current -> future
                var slope = (current - avg2010) / yearsBaseToCurrent;
                predicted = current + slope * years;
                predicted = Math.Clamp(predicted, 0.0, 20.0);
            }
            else
            {
                // Heuristic when no 2010 data for this state
                var drift = (nationalMean - current) * 0.5 * factor;
                predicted = current + drift;
            }

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
