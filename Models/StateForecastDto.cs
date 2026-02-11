namespace AlabamaWalkabilityApi.Models;

/// <summary>
/// Heuristic forecast of average walkability by state.
/// NOTE: This is a simple speculative model based on current scores only.
/// </summary>
public record StateForecastDto(
    string StateFips,
    double CurrentAvgWalkability,
    double PredictedAvgWalkability,
    int BlockGroupCount
);

