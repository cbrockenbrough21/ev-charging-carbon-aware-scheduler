namespace EvCharging.Core.Domain;

public sealed record ChargingRecommendation(
    string Zone,
    DateTime RecommendedStartUtc,
    DateTime RecommendedEndUtc,
    double EstimatedEmissionsKg,
    string Explanation
);
