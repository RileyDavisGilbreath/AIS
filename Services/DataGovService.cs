using System.Text.Json;

namespace AlabamaWalkabilityApi.Services;

public class DataGovService : IDataGovService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private const string DefaultBaseUrl = "https://catalog.data.gov/api/3";

    public DataGovService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _http.BaseAddress = new Uri(config["DataGov:BaseUrl"] ?? DefaultBaseUrl);
        _apiKey = config["DataGov:ApiKey"] ?? "DEMO_KEY";
    }

    public async Task<DataGovSearchResult> SearchAsync(string query, int rows = 10, CancellationToken ct = default)
    {
        // IMPORTANT: no leading '/' or HttpClient will drop the '/api/3' base path
        var url = $"action/package_search?q={Uri.EscapeDataString(query)}&rows={rows}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(_apiKey))
            req.Headers.Add("x-api-key", _apiKey);

        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();

        var root = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var result = root.RootElement.GetProperty("result");
        var count = result.TryGetProperty("count", out var c) ? c.GetInt32() : 0;
        var results = result.GetProperty("results");

        var packages = new List<DataGovPackage>();
        foreach (var p in results.EnumerateArray())
        {
            var resources = new List<DataGovResource>();
            if (p.TryGetProperty("resources", out var resArr))
            {
                foreach (var r in resArr.EnumerateArray())
                {
                    resources.Add(new DataGovResource
                    {
                        Id = r.TryGetProperty("id", out var resId) ? resId.GetString() ?? "" : "",
                        Url = r.TryGetProperty("url", out var resUrl) ? resUrl.GetString() ?? "" : "",
                        Format = r.TryGetProperty("format", out var resFormat) ? resFormat.GetString() : null,
                        Name = r.TryGetProperty("name", out var resName) ? resName.GetString() : null
                    });
                }
            }
            packages.Add(new DataGovPackage
            {
                Id = p.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                Name = p.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                Title = p.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                Notes = p.TryGetProperty("notes", out var notes) ? notes.GetString() : null,
                Resources = resources
            });
        }

        return new DataGovSearchResult { Count = count, Packages = packages };
    }

    public async Task<string> GetResourceAsync(string resourceUrl, CancellationToken ct = default)
    {
        const int maxAttempts = 4;
        Exception? lastEx = null;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var res = await _http.GetAsync(resourceUrl, ct);
                res.EnsureSuccessStatusCode();
                return await res.Content.ReadAsStringAsync(ct);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                lastEx = ex;
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), ct);
            }
            catch (IOException ex) when (attempt < maxAttempts)
            {
                lastEx = ex;
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), ct);
            }
        }
        throw lastEx!;
    }
}
