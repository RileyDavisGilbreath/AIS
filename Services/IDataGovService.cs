namespace AlabamaWalkabilityApi.Services;

public interface IDataGovService
{
    /// <summary>Search data.gov catalog; returns metadata including resource URLs.</summary>
    Task<DataGovSearchResult> SearchAsync(string query, int rows = 10, CancellationToken ct = default);

    /// <summary>Fetch raw content from a resource URL (CSV, JSON, etc.).</summary>
    Task<string> GetResourceAsync(string resourceUrl, CancellationToken ct = default);
}

public class DataGovSearchResult
{
    public int Count { get; init; }
    public IReadOnlyList<DataGovPackage> Packages { get; init; } = [];
}

public class DataGovPackage
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Notes { get; init; }
    public IReadOnlyList<DataGovResource> Resources { get; init; } = [];
}

public class DataGovResource
{
    public string Id { get; init; } = "";
    public string Url { get; init; } = "";
    public string? Format { get; init; }
    public string? Name { get; init; }
}
