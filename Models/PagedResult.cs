namespace AlabamaWalkabilityApi.Models;

public record PagedResult<T>(int TotalCount, IEnumerable<T> Items);
