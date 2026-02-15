namespace AlabamaWalkabilityApi.Services;

public interface IWalkabilityImportService
{
    /// <summary>Fetch CSV from URL and import into DB. Returns (blockGroups, counties).</summary>
    Task<(int blockGroups, int counties)> ImportFromUrlAsync(string resourceUrl, CancellationToken ct = default);

    /// <summary>Import raw CSV content into DB. Returns (blockGroups, counties).</summary>
    Task<(int blockGroups, int counties)> ImportFromCsvAsync(string csv, CancellationToken ct = default);

    /// <summary>Import CSV from stream (e.g. uploaded file) without loading entire file into memory.</summary>
    Task<(int blockGroups, int counties)> ImportFromStreamAsync(Stream stream, CancellationToken ct = default);

}
