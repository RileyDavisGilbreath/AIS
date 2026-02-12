using AlabamaWalkabilityApi.Models;

namespace AlabamaWalkabilityApi.Services;

public interface IWalkabilityService
{
    Task<PagedResult<CountyDto>> GetCountiesAsync(string? sort, int limit, CancellationToken ct = default);
    Task<StateStatsDto?> GetStateStatsAsync(CancellationToken ct = default);
    Task<IEnumerable<CountyDto>> GetCountyStatsAsync(string? sort, bool withDataOnly = false, CancellationToken ct = default);
    Task<IEnumerable<ScoreBucket>> GetDistributionAsync(string? countyFips, CancellationToken ct = default);
    Task<IEnumerable<StateForecastDto>> GetStateForecastAsync(int years, CancellationToken ct = default);
    Task<IEnumerable<StateRecommendationDto>> GetStateRecommendationsAsync(CancellationToken ct = default);
}
