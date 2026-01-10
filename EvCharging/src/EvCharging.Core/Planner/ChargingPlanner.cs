using EvCharging.Core.Domain;

namespace EvCharging.Core.Planner;

public sealed class ChargingPlanner
{
    // tiny tolerance so floating point rounding doesn’t cause “fake ties”
    private const double Epsilon = 1e-9;

    public ChargingRecommendation Recommend(
        ChargingRequest request,
        IReadOnlyList<CarbonIntensityPoint> hourlyIntensity)
    {
        ValidateRequest(request);
        if (hourlyIntensity is null || hourlyIntensity.Count == 0)
            throw new ArgumentException("Carbon intensity series is empty.");

        // Build a fast lookup: timestamp -> intensity
        // Assumption: inputs are hourly points with unique timestamps
        var intensityByHour = hourlyIntensity.ToDictionary(p => EnsureUtc(p.TimestampUtc), p => p.KgCo2PerKwh);

        var windowStart = RoundDownToHour(EnsureUtc(request.WindowStartUtc));
        var windowEnd = EnsureUtc(request.WindowEndUtc);

        // Duration in hours (can be fractional)
        var durationHours = request.KWhNeeded / request.MaxChargingKw;
        if (durationHours <= 0) throw new ArgumentException("Charging duration must be > 0.");

        DateTime? bestStart = null;
        DateTime? bestEnd = null;
        double bestEmissions = double.PositiveInfinity;

        // Enumerate hourly candidate starts within the window.
        // Candidate start must be >= windowStartUtc and < windowEndUtc.
        for (var candidateStart = windowStart;
             candidateStart < windowEnd;
             candidateStart = candidateStart.AddHours(1))
        {
            // Candidate must start within original window (not rounded).
            if (candidateStart < EnsureUtc(request.WindowStartUtc)) continue;

            var candidateEnd = candidateStart.AddHours(durationHours);

            // Must finish by window end
            if (candidateEnd > windowEnd) continue;

            // Compute emissions for this candidate
            if (!TryComputeEmissionsKg(
                    candidateStart,
                    durationHours,
                    request.MaxChargingKw,
                    intensityByHour,
                    out var emissionsKg))
            {
                // Not enough data coverage for required hours
                continue;
            }

            // Minimize emissions; tie-break by earliest start
            var isBetter = emissionsKg < bestEmissions - Epsilon;
            var isTieAndEarlier = Math.Abs(emissionsKg - bestEmissions) <= Epsilon
                                  && (bestStart is null || candidateStart < bestStart.Value);

            if (isBetter || isTieAndEarlier)
            {
                bestStart = candidateStart;
                bestEnd = candidateEnd;
                bestEmissions = emissionsKg;
            }
        }

        if (bestStart is null || bestEnd is null || double.IsInfinity(bestEmissions))
        {
            throw new InvalidOperationException(
                "No feasible charging start time found within the window given available carbon intensity data.");
        }

        var explanation =
            "Hourly planner: evaluates hourly start times and chooses the feasible window with minimum estimated emissions. " +
            "Tie-break: earliest start time. Carbon intensity series assumed hourly in UTC.";

        return new ChargingRecommendation(
            request.Zone,
            bestStart.Value,
            bestEnd.Value,
            bestEmissions,
            explanation);
    }

    private static bool TryComputeEmissionsKg(
        DateTime startUtc,
        double durationHours,
        double maxChargingKw,
        IReadOnlyDictionary<DateTime, double> intensityByHour,
        out double emissionsKg)
    {
        emissionsKg = 0.0;

        //We move hour by hour. Each hour delivers up to maxChargingKw * 1h kWh,
        // except the last hour may be fractional.
        var remainingHours = durationHours;
        var currentHour = RoundDownToHour(startUtc);

        while (remainingHours > 0)
        {
            var hoursThisBucket = Math.Min(1.0, remainingHours);

            if (!intensityByHour.TryGetValue(currentHour, out var kgPerKwh))
            {
                emissionsKg = 0.0;
                return false; // missing data coverage
            }

            var energyKwh = maxChargingKw * hoursThisBucket;
            emissionsKg += energyKwh * kgPerKwh;

            remainingHours -= hoursThisBucket;
            currentHour = currentHour.AddHours(1);
        }

        return true;
    }

    private static void ValidateRequest(ChargingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Zone))
            throw new ArgumentException("Zone is required.");

        var start = EnsureUtc(request.WindowStartUtc);
        var end = EnsureUtc(request.WindowEndUtc);

        if (end <= start)
            throw new ArgumentException("WindowEndUtc must be after WindowStartUtc.");

        if (request.KWhNeeded <= 0)
            throw new ArgumentException("KWhNeeded must be > 0.");

        if (request.MaxChargingKw <= 0)
            throw new ArgumentException("MaxChargingKw must be > 0.");
    }

    private static DateTime RoundDownToHour(DateTime utc)
        => new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);

    private static DateTime EnsureUtc(DateTime dt)
    {
        // You can decide to be strict here; for MVP this makes behavior deterministic.
        if (dt.Kind == DateTimeKind.Utc) return dt;

        if (dt.Kind == DateTimeKind.Local)
            return dt.ToUniversalTime();

        // Unspecified: treat as UTC to avoid silent local-time bugs, but you could also throw.
        return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    }
}
