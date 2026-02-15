using MySql.Data.MySqlClient;
using System.Globalization;

namespace AlabamaWalkabilityApi.Services;

    public class WalkabilityImportService : IWalkabilityImportService
{
    private readonly IDataGovService _dataGov;
    private readonly WalkabilityDbContext _db;

    public WalkabilityImportService(IDataGovService dataGov, WalkabilityDbContext db)
    {
        _dataGov = dataGov;
        _db = db;
    }

    public async Task<(int blockGroups, int counties)> ImportFromUrlAsync(string resourceUrl, CancellationToken ct = default)
    {
        var csv = await _dataGov.GetResourceAsync(resourceUrl, ct);
        return await ImportFromCsvAsync(csv, ct);
    }

    public async Task<(int blockGroups, int counties)> ImportFromStreamAsync(Stream stream, CancellationToken ct = default)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);

        using var reader = new StreamReader(stream);
        var headerLine = await reader.ReadLineAsync(ct);
        if (string.IsNullOrEmpty(headerLine))
            throw new Exception("CSV has no header row");

        var headers = headerLine.Split(',').Select(h => h.Trim('"', ' ')).ToArray();
        var geoidIdx = IndexOfHeader(headers, "GEOID", "GEOID10", "GEOID20", "BlkGrpID", "geoid", "FIPS");
        var walkIdx = IndexOfHeader(headers, "NatWalkInd", "natwalkind", "WalkIndex", "NWI", "WALK_INDEX", "Walkability");
        var popIdx = IndexOfHeader(headers, "D1B", "Pop2010", "POP2010", "population", "POP", "TOTPOP", "TotPop", "P001001", "B01003_001E", "Total_Population", "TotalPopulation");
        var housingIdx = IndexOfHeader(headers, "D1A", "HU2010", "housing_units", "HU", "HU2010");

        if (geoidIdx < 0 || walkIdx < 0)
            throw new Exception($"CSV missing required columns. Need GEOID (or GEOID10/GEOID20) and NatWalkInd (or WalkIndex). Found: {string.Join(", ", headers)}");

        var blockGroupCount = 0;
        var countyStats = new Dictionary<(string StateFips, string CountyFips), (double totalWalk, int count, int pop, int housing)>();

        var insertCmd = new MySqlCommand("""
            INSERT INTO block_groups (fips, state_fips, county_fips, tract_fips, walkability_score, population, housing_units)
            VALUES (@fips, @state, @county, @tract, @walk, @pop, @housing)
            ON DUPLICATE KEY UPDATE
                walkability_score = VALUES(walkability_score),
                population = VALUES(population),
                housing_units = VALUES(housing_units)
            """, conn);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            var cols = ParseCsvLine(line);
            if (cols.Length <= Math.Max(geoidIdx, walkIdx)) continue;

            var geoid = cols[geoidIdx].Trim('"', ' ');
            if (string.IsNullOrEmpty(geoid)) continue;

            var fips = NormalizeGeoid(geoid);
            var stateFips = fips.Length >= 2 ? fips.Substring(0, 2) : "00";
            var countyFips = fips.Length >= 5 ? fips.Substring(2, 3) : "000";
            var tractFips = fips.Length >= 11 ? fips.Substring(0, 11) : fips.PadRight(11, '0');

            if (!double.TryParse(cols[walkIdx].Trim('"', ' '), NumberStyles.Float, CultureInfo.InvariantCulture, out var walkScore))
                continue;

            var pop = popIdx >= 0 && int.TryParse(cols[popIdx].Trim('"', ' '), out var p) ? p : 0;
            var housing = housingIdx >= 0 && int.TryParse(cols[housingIdx].Trim('"', ' '), out var h) ? h : 0;

            insertCmd.Parameters.Clear();
            insertCmd.Parameters.AddWithValue("@fips", fips);
            insertCmd.Parameters.AddWithValue("@state", stateFips);
            insertCmd.Parameters.AddWithValue("@county", countyFips);
            insertCmd.Parameters.AddWithValue("@tract", tractFips);
            insertCmd.Parameters.AddWithValue("@walk", walkScore);
            insertCmd.Parameters.AddWithValue("@pop", pop);
            insertCmd.Parameters.AddWithValue("@housing", housing);
            await insertCmd.ExecuteNonQueryAsync(ct);

            blockGroupCount++;
            var key = (stateFips, countyFips);
            if (!countyStats.ContainsKey(key))
                countyStats[key] = (0, 0, 0, 0);
            var stats = countyStats[key];
            countyStats[key] = (stats.totalWalk + walkScore, stats.count + 1, stats.pop + pop, stats.housing + housing);
        }

        var countyUpdateCmd = new MySqlCommand("""
            INSERT INTO counties (state_fips, fips, name, avg_walkability, block_group_count, population)
            VALUES (@state, @fips, @name, @avg, @count, @pop)
            ON DUPLICATE KEY UPDATE
                name = VALUES(name),
                avg_walkability = VALUES(avg_walkability),
                block_group_count = VALUES(block_group_count),
                population = VALUES(population)
            """, conn);

        var countyCount = 0;
        foreach (var (keyStateCounty, (totalWalk, count, pop, _)) in countyStats)
        {
            var (stateFips, countyFips) = keyStateCounty;
            var avgWalk = count > 0 ? totalWalk / count : 0;
            var name = $"County {stateFips}{countyFips}";

            countyUpdateCmd.Parameters.Clear();
            countyUpdateCmd.Parameters.AddWithValue("@state", stateFips);
            countyUpdateCmd.Parameters.AddWithValue("@fips", countyFips);
            countyUpdateCmd.Parameters.AddWithValue("@name", name);
            countyUpdateCmd.Parameters.AddWithValue("@avg", avgWalk);
            countyUpdateCmd.Parameters.AddWithValue("@count", count);
            countyUpdateCmd.Parameters.AddWithValue("@pop", pop);
            await countyUpdateCmd.ExecuteNonQueryAsync(ct);
            countyCount++;
        }

        return (blockGroupCount, countyCount);
    }

    public async Task<(int blockGroups, int counties)> ImportFromCsvAsync(string csv, CancellationToken ct = default)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) throw new Exception("CSV has no data rows");

        var headers = lines[0].Split(',').Select(h => h.Trim('"', ' ')).ToArray();
        var geoidIdx = IndexOfHeader(headers, "GEOID", "GEOID10", "GEOID20", "BlkGrpID", "geoid", "FIPS");
        var walkIdx = IndexOfHeader(headers, "NatWalkInd", "natwalkind", "WalkIndex", "NWI", "WALK_INDEX", "Walkability");
        var popIdx = IndexOfHeader(headers, "D1B", "Pop2010", "POP2010", "population", "POP", "TOTPOP", "TotPop", "P001001", "B01003_001E", "Total_Population", "TotalPopulation");
        var housingIdx = IndexOfHeader(headers, "D1A", "HU2010", "housing_units", "HU", "HU2010");
        var stateFipsIdx = IndexOfHeader(headers, "STATEFP", "STATE", "state_fips", "StateFIPS");

        if (geoidIdx < 0 || walkIdx < 0)
            throw new Exception($"CSV missing required columns. Need GEOID/GEOID10/GEOID20 and NatWalkInd/WalkIndex. Found headers: {string.Join(", ", headers)}");

        var blockGroupCount = 0;
        // Keyed by (state_fips, county_fips) so we can handle national data
        var countyStats = new Dictionary<(string StateFips, string CountyFips), (double totalWalk, int count, int pop, int housing)>();

        var insertCmd = new MySqlCommand("""
            INSERT INTO block_groups (fips, state_fips, county_fips, tract_fips, walkability_score, population, housing_units)
            VALUES (@fips, @state, @county, @tract, @walk, @pop, @housing)
            ON DUPLICATE KEY UPDATE
                walkability_score = VALUES(walkability_score),
                population = VALUES(population),
                housing_units = VALUES(housing_units)
            """, conn);

        for (int i = 1; i < lines.Length; i++)
        {
            var cols = ParseCsvLine(lines[i]);
            if (cols.Length <= Math.Max(geoidIdx, walkIdx)) continue;

            var geoid = cols[geoidIdx].Trim('"', ' ');
            if (string.IsNullOrEmpty(geoid)) continue;

            var fips = NormalizeGeoid(geoid);
            var stateFips = fips.Length >= 2 ? fips.Substring(0, 2) : "00";
            var countyFips = fips.Length >= 5 ? fips.Substring(2, 3) : "000";
            var tractFips = fips.Length >= 11 ? fips.Substring(0, 11) : fips.PadRight(11, '0');

            if (!double.TryParse(cols[walkIdx].Trim('"', ' '), NumberStyles.Float, CultureInfo.InvariantCulture, out var walkScore))
                continue;

            var pop = popIdx >= 0 && int.TryParse(cols[popIdx].Trim('"', ' '), out var p) ? p : 0;
            var housing = housingIdx >= 0 && int.TryParse(cols[housingIdx].Trim('"', ' '), out var h) ? h : 0;

            insertCmd.Parameters.Clear();
            insertCmd.Parameters.AddWithValue("@fips", fips);
            insertCmd.Parameters.AddWithValue("@state", stateFips);
            insertCmd.Parameters.AddWithValue("@county", countyFips);
            insertCmd.Parameters.AddWithValue("@tract", tractFips);
            insertCmd.Parameters.AddWithValue("@walk", walkScore);
            insertCmd.Parameters.AddWithValue("@pop", pop);
            insertCmd.Parameters.AddWithValue("@housing", housing);
            await insertCmd.ExecuteNonQueryAsync(ct);

            blockGroupCount++;
            var key = (stateFips, countyFips);
            if (!countyStats.ContainsKey(key))
                countyStats[key] = (0, 0, 0, 0);
            var stats = countyStats[key];
            countyStats[key] = (stats.totalWalk + walkScore, stats.count + 1, stats.pop + pop, stats.housing + housing);
        }

        var countyUpdateCmd = new MySqlCommand("""
            INSERT INTO counties (state_fips, fips, name, avg_walkability, block_group_count, population)
            VALUES (@state, @fips, @name, @avg, @count, @pop)
            ON DUPLICATE KEY UPDATE
                name = VALUES(name),
                avg_walkability = VALUES(avg_walkability),
                block_group_count = VALUES(block_group_count),
                population = VALUES(population)
            """, conn);

        var countyCount = 0;
        foreach (var (keyStateCounty, (totalWalk, count, pop, _)) in countyStats)
        {
            var (stateFips, countyFips) = keyStateCounty;
            var avgWalk = count > 0 ? totalWalk / count : 0;
            var name = $"County {stateFips}{countyFips}";

            countyUpdateCmd.Parameters.Clear();
            countyUpdateCmd.Parameters.AddWithValue("@state", stateFips);
            countyUpdateCmd.Parameters.AddWithValue("@fips", countyFips);
            countyUpdateCmd.Parameters.AddWithValue("@name", name);
            countyUpdateCmd.Parameters.AddWithValue("@avg", avgWalk);
            countyUpdateCmd.Parameters.AddWithValue("@count", count);
            countyUpdateCmd.Parameters.AddWithValue("@pop", pop);
            await countyUpdateCmd.ExecuteNonQueryAsync(ct);
            countyCount++;
        }

        return (blockGroupCount, countyCount);
    }

    /// <summary>Parse GEOID (handles scientific notation like 4.8113E+11 from EPA CSV).</summary>
    private static string NormalizeGeoid(string geoid)
    {
        geoid = geoid.Trim('"', ' ');
        if (double.TryParse(geoid, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
        {
            var s = ((long)num).ToString(CultureInfo.InvariantCulture);
            return s.Length >= 12 ? s.Substring(0, 12) : s.PadLeft(12, '0');
        }
        return geoid.Length >= 12 ? geoid.Substring(0, 12) : geoid.PadLeft(12, '0');
    }

    private static int IndexOfHeader(string[] headers, params string[] names)
    {
        foreach (var name in names)
        {
            var i = Array.FindIndex(headers, h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) return i;
        }
        return -1;
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = "";
        var inQuotes = false;
        foreach (var c in line)
        {
            if (c == '"')
                inQuotes = !inQuotes;
            else if (c == ',' && !inQuotes)
            {
                result.Add(current);
                current = "";
            }
            else
                current += c;
        }
        result.Add(current);
        return result.ToArray();
    }

}
