namespace AlabamaWalkabilityApi.Models;

public record ApiError(string Error, string Code, Dictionary<string, string[]>? Details = null);
