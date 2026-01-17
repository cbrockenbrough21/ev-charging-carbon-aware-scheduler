namespace EvCharging.API.Contracts;

public class ChargingRecommendationResponse
{
    public string Zone { get; init; } = string.Empty;
    public DateTime RecommendedStartUtc { get; init; }
    public DateTime RecommendedEndUtc { get; init; }
    public decimal EstimatedEmissionsKg { get; init; }
    public AssumptionsDto Assumptions { get; init; } = new();
}
