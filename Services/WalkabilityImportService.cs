using MySql.Data.MySqlClient;
using System.Globalization;

namespace AlabamaWalkabilityApi.Services;

    public class WalkabilityImportService : IWalkabilityImportService
{
    // Static lookup of Alabama county FIPS -> county name
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

            var fips = geoid.Length >= 12 ? geoid.Substring(0, 12) : geoid.PadLeft(12, '0');
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

        var countyNames = await GetCountyNames(conn, ct);
        var countyCount = 0;
        foreach (var (keyStateCounty, (totalWalk, count, pop, _)) in countyStats)
        {
            var (stateFips, countyFips) = keyStateCounty;
            var avgWalk = count > 0 ? totalWalk / count : 0;
            var name = stateFips == "01"
                ? countyNames.GetValueOrDefault(countyFips, $"County {countyFips}")
                : $"County {stateFips}{countyFips}";

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

    public async Task<int> SeedAlabamaCountiesAsync(CancellationToken ct = default)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);
        var cmd = new MySqlCommand("""
            INSERT INTO counties (state_fips, fips, name, avg_walkability, block_group_count, population)
            VALUES ('01', @fips, @name, 0, 0, 0)
            ON DUPLICATE KEY UPDATE name = VALUES(name)
            """, conn);
        var count = 0;
        foreach (var (fips, name) in AlabamaCountyNames)
        {
            if (name == "Other") continue;
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@fips", fips);
            cmd.Parameters.AddWithValue("@name", name);
            await cmd.ExecuteNonQueryAsync(ct);
            count++;
        }
        return count;
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

            // For national import, keep all states; derive state_fips from GEOID
            var fips = geoid.Length >= 12 ? geoid.Substring(0, 12) : geoid.PadLeft(12, '0');
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

        var countyNames = await GetCountyNames(conn, ct);
        var countyCount = 0;
        foreach (var (keyStateCounty, (totalWalk, count, pop, _)) in countyStats)
        {
            var (stateFips, countyFips) = keyStateCounty;
            var avgWalk = count > 0 ? totalWalk / count : 0;
            // We only have canonical names for Alabama; elsewhere fall back to generic
            var name = stateFips == "01"
                ? countyNames.GetValueOrDefault(countyFips, $"County {countyFips}")
                : $"County {stateFips}{countyFips}";

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

    private static string NormalizeCountyFips(string fips)
    {
        if (string.IsNullOrWhiteSpace(fips)) return "000";
        fips = fips.Trim();
        return fips.Length >= 3 ? fips : fips.PadLeft(3, '0');
    }

    private static async Task<Dictionary<string, string>> GetCountyNames(MySqlConnection conn, CancellationToken ct)
    {
        // Start with hard-coded Alabama county names (always 3-digit keys: 001, 003, ...)
        var dict = new Dictionary<string, string>(AlabamaCountyNames);

        // Overlay from DB using normalized 3-digit key; only overwrite if DB has a non-empty name
        var cmd = new MySqlCommand("SELECT fips, name FROM counties WHERE state_fips = '01'", conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var key = NormalizeCountyFips(r.IsDBNull(0) ? "" : (r.GetString(0) ?? ""));
            var name = r.IsDBNull(1) ? null : r.GetString(1);
            if (!string.IsNullOrWhiteSpace(name))
                dict[key] = name.Trim();
        }
        return dict;
    }
}
