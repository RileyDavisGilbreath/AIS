namespace AlabamaWalkabilityApi.Models;

public record CountyDto(
    string Fips,
    string Name,
    double AvgWalkabilityScore,
    int BlockGroupCount,
    int Population
);
