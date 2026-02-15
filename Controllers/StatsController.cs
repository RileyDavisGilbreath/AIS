using Microsoft.AspNetCore.Mvc;
using AlabamaWalkabilityApi.Models;
using AlabamaWalkabilityApi.Services;

namespace AlabamaWalkabilityApi.Controllers;

[ApiController]
[Route("api/stats")]
public class StatsController : ControllerBase
{
    private readonly IWalkabilityService _svc;

    public StatsController(IWalkabilityService svc) => _svc = svc;

    /// <summary>Statewide summary stats.</summary>
    [HttpGet("state")]
    [ProducesResponseType(typeof(StateStatsDto), 200)]
    public async Task<ActionResult<StateStatsDto>> GetStateStats(CancellationToken ct = default)
    {
        try
        {
            var result = await _svc.GetStateStatsAsync(ct);
            return Ok(result ?? new StateStatsDto(0, 0, 0, 0, new List<ScoreBucket>()));
        }
        catch (Exception)
        {
            return Ok(new StateStatsDto(0, 0, 0, 0, new List<ScoreBucket>()));
        }
    }

    /// <summary>Per-county stats for charts. Optional stateFips to filter. Use withDataOnly=true to exclude counties with no block-group/population data.</summary>
    [HttpGet("counties")]
    [ProducesResponseType(typeof(IEnumerable<CountyDto>), 200)]
    public async Task<ActionResult<IEnumerable<CountyDto>>> GetCountyStats(
        [FromQuery] string? stateFips,
        [FromQuery] string? sort,
        [FromQuery] bool withDataOnly = false,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _svc.GetCountyStatsAsync(stateFips, sort, withDataOnly, ct);
            return Ok(result ?? Array.Empty<CountyDto>());
        }
        catch (Exception)
        {
            return Ok(Array.Empty<CountyDto>());
        }
    }

    /// <summary>Walkability score distribution histogram. Optional stateFips and countyFips to filter.</summary>
    [HttpGet("distribution")]
    [ProducesResponseType(typeof(IEnumerable<ScoreBucket>), 200)]
    public async Task<ActionResult<IEnumerable<ScoreBucket>>> GetDistribution(
        [FromQuery] string? stateFips,
        [FromQuery] string? countyFips,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _svc.GetDistributionAsync(stateFips, countyFips, ct);
            return Ok(result ?? Array.Empty<ScoreBucket>());
        }
        catch (Exception)
        {
            return Ok(Array.Empty<ScoreBucket>());
        }
    }

    /// <summary>
    /// Heuristic forecast of average walkability by state for the next N years.
    /// NOTE: This is a simple speculative model using current scores only.
    /// </summary>
    [HttpGet("state-forecast")]
    [ProducesResponseType(typeof(IEnumerable<StateForecastDto>), 200)]
    public async Task<ActionResult<IEnumerable<StateForecastDto>>> GetStateForecast(
        [FromQuery] int years = 10,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _svc.GetStateForecastAsync(years, ct);
            return Ok(result ?? Array.Empty<StateForecastDto>());
        }
        catch (Exception)
        {
            return Ok(Array.Empty<StateForecastDto>());
        }
    }

    /// <summary>Prescriptive recommendations for states with lowest walkability (prioritize improvements).</summary>
    [HttpGet("state-recommendations")]
    [ProducesResponseType(typeof(IEnumerable<StateRecommendationDto>), 200)]
    public async Task<ActionResult<IEnumerable<StateRecommendationDto>>> GetStateRecommendations(CancellationToken ct = default)
    {
        try
        {
            var result = await _svc.GetStateRecommendationsAsync(ct);
            return Ok(result ?? Array.Empty<StateRecommendationDto>());
        }
        catch (Exception)
        {
            return Ok(Array.Empty<StateRecommendationDto>());
        }
    }

    /// <summary>Test endpoint - check DB connection and row counts.</summary>
    [HttpGet("test")]
    public async Task<ActionResult> TestDb(CancellationToken ct = default)
    {
        try
        {
            var stateStats = await _svc.GetStateStatsAsync(ct);
            var counties = await _svc.GetCountyStatsAsync(null, null, false, ct);
            return Ok(new
            {
                connected = true,
                stateStats = stateStats != null ? new
                {
                    stateStats.BlockGroupCount,
                    stateStats.Population,
                    stateStats.AvgWalkabilityScore
                } : null,
                countyCount = counties.Count()
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
