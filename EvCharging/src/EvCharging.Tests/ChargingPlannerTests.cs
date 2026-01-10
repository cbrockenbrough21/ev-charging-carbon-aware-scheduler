using EvCharging.Core.Domain;
using EvCharging.Core.Planner;
using Xunit;

namespace EvCharging.Tests;

public class ChargingPlannerTests
{
    [Fact]
    public void ConstantIntensity_TieBreaksToEarliestStart()
    {
        // Window: 00:00 to 06:00 UTC, need 2 hours at 5 kW -> 10 kWh
        var req = new ChargingRequest(
            "CAISO",
            new DateTime(2026, 01, 05, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 01, 05, 6, 0, 0, DateTimeKind.Utc),
            KWhNeeded: 10.0,
            MaxChargingKw: 5.0
        );

        // Flat intensity across all hours
        var points = HourlyPoints(
            startUtc: new DateTime(2026, 01, 05, 0, 0, 0, DateTimeKind.Utc),
            hours: 6,
            kgPerKwh: h => 0.5
        );

        var planner = new ChargingPlanner();
        var rec = planner.Recommend(req, points);

        Assert.Equal(new DateTime(2026, 01, 05, 0, 0, 0, DateTimeKind.Utc), rec.RecommendedStartUtc);
        Assert.Equal(new DateTime(2026, 01, 05, 2, 0, 0, DateTimeKind.Utc), rec.RecommendedEndUtc);
    }

    [Fact]
    public void AvoidsHighEmissionsSpike()
    {
        var req = new ChargingRequest(
            "CAISO",
            new DateTime(2026, 01, 05, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 01, 05, 6, 0, 0, DateTimeKind.Utc),
            KWhNeeded: 10.0,
            MaxChargingKw: 5.0 // 2 hours
        );

        // Make hour 1 (01:00) very dirty, others clean.
        var points = HourlyPoints(
            new DateTime(2026, 01, 05, 0, 0, 0, DateTimeKind.Utc),
            6,
            kgPerKwh: h => h == 1 ? 5.0 : 0.1
        );

        var planner = new ChargingPlanner();
        var rec = planner.Recommend(req, points);

        // Starting at 00:00 uses hours 00 and 01 (bad).
        // Best should start at 02:00 uses hours 02 and 03 (clean).
        Assert.Equal(new DateTime(2026, 01, 05, 2, 0, 0, DateTimeKind.Utc), rec.RecommendedStartUtc);
        Assert.Equal(new DateTime(2026, 01, 05, 4, 0, 0, DateTimeKind.Utc), rec.RecommendedEndUtc);
    }

    [Fact]
    public void SupportsPartialFinalHour()
    {
        var req = new ChargingRequest(
            "CAISO",
            new DateTime(2026, 01, 05, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 01, 05, 6, 0, 0, DateTimeKind.Utc),
            KWhNeeded: 12.5,
            MaxChargingKw: 5.0 // 2.5 hours
        );

        var points = HourlyPoints(
            new DateTime(2026, 01, 05, 0, 0, 0, DateTimeKind.Utc),
            6,
            kgPerKwh: h => 1.0 // simple
        );

        var planner = new ChargingPlanner();
        var rec = planner.Recommend(req, points);

        Assert.Equal(new DateTime(2026, 01, 05, 0, 0, 0, DateTimeKind.Utc), rec.RecommendedStartUtc);
        Assert.Equal(new DateTime(2026, 01, 05, 2, 30, 0, DateTimeKind.Utc), rec.RecommendedEndUtc);

        // Emissions: 12.5 kWh * 1 kg/kWh = 12.5 kg
        Assert.True(Math.Abs(rec.EstimatedEmissionsKg - 12.5) < 1e-9);
    }

    [Fact]
    public void ThrowsWhenWindowInfeasible()
    {
        var req = new ChargingRequest(
            "CAISO",
            new DateTime(2026, 01, 05, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 01, 05, 1, 0, 0, DateTimeKind.Utc),
            KWhNeeded: 10.0,
            MaxChargingKw: 5.0 // needs 2 hours but window is 1 hour
        );

        var points = HourlyPoints(
            new DateTime(2026, 01, 05, 0, 0, 0, DateTimeKind.Utc),
            3,
            kgPerKwh: h => 0.5
        );

        var planner = new ChargingPlanner();

        Assert.Throws<InvalidOperationException>(() => planner.Recommend(req, points));
    }

    private static List<CarbonIntensityPoint> HourlyPoints(DateTime startUtc, int hours, Func<int, double> kgPerKwh)
    {
        var list = new List<CarbonIntensityPoint>();
        for (int i = 0; i < hours; i++)
        {
            list.Add(new CarbonIntensityPoint(startUtc.AddHours(i), kgPerKwh(i)));
        }
        return list;
    }
}
