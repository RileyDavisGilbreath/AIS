namespace AlabamaWalkabilityApi.Models;

public record StateRecommendationDto(
    string StateFips,
    string StateAbbrev,
    double CurrentScore,
    string Priority,
    string Recommendation
);
