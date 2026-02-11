using Microsoft.AspNetCore.Mvc;
using AlabamaWalkabilityApi.Models;
using AlabamaWalkabilityApi.Services;

namespace AlabamaWalkabilityApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CountiesController : ControllerBase
{
    private readonly IWalkabilityService _svc;

    public CountiesController(IWalkabilityService svc) => _svc = svc;

    /// <summary>List all Alabama counties with summary stats.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<CountyDto>), 200)]
    public async Task<ActionResult<PagedResult<CountyDto>>> GetCounties(
        [FromQuery] string? sort,
        [FromQuery] int limit = 67,
        CancellationToken ct = default)
    {
        var result = await _svc.GetCountiesAsync(sort, limit, ct);
        return Ok(result);
    }
}
