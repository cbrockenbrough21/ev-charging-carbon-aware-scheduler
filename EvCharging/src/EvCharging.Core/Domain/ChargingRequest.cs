namespace EvCharging.Core.Domain;

public sealed record ChargingRequest(
    string Zone,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    double KWhNeeded,
    double MaxChargingKw
);
