using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using AlabamaWalkabilityApi.Services;

namespace AlabamaWalkabilityApi.Controllers;

[ApiController]
[Route("api/import")]
public class ImportController : ControllerBase
{
    private readonly IDataGovService _dataGov;
    private readonly IWalkabilityImportService _import;
    private readonly IWebHostEnvironment _env;

    public ImportController(IDataGovService dataGov, IWalkabilityImportService import, IWebHostEnvironment env)
    {
        _dataGov = dataGov;
        _import = import;
        _env = env;
    }

    /// <summary>Seed the 67 Alabama counties with names (zero stats). Call once if /api/stats/counties is empty.</summary>
    [HttpGet("seed-counties")]
    public async Task<ActionResult> SeedCounties(CancellationToken ct = default)
    {
        try
        {
            var count = await _import.SeedAlabamaCountiesAsync(ct);
            return Ok(new { seeded = count, message = $"Seeded {count} Alabama counties. Call /api/stats/counties to see them." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>Fetch first line (headers) of a CSV URL. Use to see column names if population is 0 (e.g. missing "Pop2010"/"D1B").</summary>
    [HttpGet("csv-headers")]
    public async Task<ActionResult> GetCsvHeaders([FromQuery] string url, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url))
            return BadRequest("url query parameter required");
        try
        {
            var csv = await _dataGov.GetResourceAsync(url, ct);
            var firstLine = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            var headers = firstLine.Split(',').Select(h => h.Trim('"', ' ')).ToArray();
            return Ok(new { headers, note = "If population is always 0, ensure one of these matches a population column (e.g. Pop2010, D1B, POP, population)." });
        }
        catch (Exception ex)
        {
            return Ok(new { error = ex.Message });
        }
    }

    /// <summary>Search data.gov for EPA walkability datasets.</summary>
    [HttpGet("search")]
    public async Task<ActionResult> SearchDatasets([FromQuery] string q = "EPA walkability Alabama", CancellationToken ct = default)
    {
        var result = await _dataGov.SearchAsync(q, rows: 10, ct);
        return Ok(result);
    }

    /// <summary>Import CSV from a data.gov resource URL. Expects CSV with GEOID, NatWalkInd, D1B (pop), D1A (housing).</summary>
    [HttpPost("csv")]
    public async Task<ActionResult> ImportCsv([FromBody] ImportRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(req.ResourceUrl))
            return BadRequest("ResourceUrl required");

        try
        {
            var (blockGroups, counties) = await _import.ImportFromUrlAsync(req.ResourceUrl, ct);
            return Ok(new { imported = new { blockGroups, counties }, message = $"Imported {blockGroups} block groups and updated {counties} counties" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    /// <summary>Import CSV via GET (easier for testing). Example: /api/import/csv?url=https://...</summary>
    [HttpGet("csv")]
    public async Task<ActionResult> ImportCsvGet([FromQuery] string url, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url))
            return BadRequest("url query parameter required");

        try
        {
            var (blockGroups, counties) = await _import.ImportFromUrlAsync(url, ct);
            return Ok(new { success = true, imported = new { blockGroups, counties }, message = $"Imported {blockGroups} block groups and updated {counties} counties" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, error = ex.Message });
        }
    }

    /// <summary>Import CSV from a file in the project Scripts folder. Example: /api/import/csv/local?file=EPA_SmartLocationDatabase_V3_Jan_2021_Final (2).csv</summary>
    [HttpGet("csv/local")]
    public async Task<ActionResult> ImportCsvLocal([FromQuery] string file, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(file))
            return BadRequest("file query parameter required (e.g. file=your.csv)");

        var fileName = Path.GetFileName(file);
        if (string.IsNullOrEmpty(fileName) || fileName != file)
            return BadRequest("file must be a simple filename, no path separators");

        var scriptsDir = Path.Combine(_env.ContentRootPath, "Scripts");
        var fullPath = Path.GetFullPath(Path.Combine(scriptsDir, fileName));
        if (!fullPath.StartsWith(Path.GetFullPath(scriptsDir), StringComparison.OrdinalIgnoreCase))
            return BadRequest("File must be in the Scripts folder");

        if (!System.IO.File.Exists(fullPath))
            return NotFound($"File not found: {fileName}");

        try
        {
            await using var stream = System.IO.File.OpenRead(fullPath);
            var (blockGroups, counties) = await _import.ImportFromStreamAsync(stream, ct);
            return Ok(new { imported = new { blockGroups, counties }, message = $"Imported {blockGroups} block groups and updated {counties} counties" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>Import CSV from uploaded file. Supports large national files (streaming). Max body 500MB.</summary>
    [HttpPost("csv/file")]
    [RequestSizeLimit(524_288_000)] // 500 MB
    public async Task<ActionResult> ImportCsvFile(IFormFile file, CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded");

        try
        {
            await using var stream = file.OpenReadStream();
            var (blockGroups, counties) = await _import.ImportFromStreamAsync(stream, ct);
            return Ok(new { imported = new { blockGroups, counties }, message = $"Imported {blockGroups} block groups and updated {counties} counties" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public record ImportRequest(string ResourceUrl);
