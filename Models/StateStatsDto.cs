namespace AlabamaWalkabilityApi.Models;

public record StateStatsDto(
    double AvgWalkabilityScore,
    double MedianScore,
    int BlockGroupCount,
    int Population,
    List<ScoreBucket> ScoreDistribution
);

public record ScoreBucket(string Bucket, int Count);
