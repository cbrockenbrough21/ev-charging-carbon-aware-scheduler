namespace EvCharging.Core.Domain;

public sealed record CarbonIntensityPoint(
    DateTime TimestampUtc,
    double KgCo2PerKwh
);