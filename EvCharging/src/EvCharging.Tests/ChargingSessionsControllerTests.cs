using EvCharging.API.Contracts;
using EvCharging.API.Controllers;
using EvCharging.Core.Domain;
using EvCharging.Core.Planner;
using EvCharging.Core.Providers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace EvCharging.Tests;

public class ChargingSessionsControllerTests
{
    private readonly Mock<ICarbonIntensityProvider> _mockProvider;
    private readonly ChargingPlanner _planner;
    private readonly ChargingSessionsController _controller;

    public ChargingSessionsControllerTests()
    {
        _mockProvider = new Mock<ICarbonIntensityProvider>();
        _planner = new ChargingPlanner();
        _controller = new ChargingSessionsController(_mockProvider.Object, _planner);
    }

    private static ChargingRecommendationRequest CreateValidRequest()
    {
        return new ChargingRecommendationRequest
        {
            Zone = "CAISO",
            WindowStartUtc = new DateTime(2025, 12, 15, 8, 0, 0, DateTimeKind.Utc),
            WindowEndUtc = new DateTime(2025, 12, 15, 18, 0, 0, DateTimeKind.Utc),
            KWhNeeded = 50m,
            MaxChargingKw = 10m
        };
    }

    #region Unsupported Zone Tests

    [Fact]
    public async Task PostRecommendation_UnsupportedZone_ReturnsBadRequest()
    {
        // Arrange
        var request = CreateValidRequest();
        request.Zone = "ERCOT";

        // Act
        var result = await _controller.PostRecommendation(request, CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);

        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("Unsupported zone", problemDetails.Title);
        Assert.Contains("CAISO", problemDetails.Detail);
        Assert.Contains("supported in MVP", problemDetails.Detail);
    }

    [Fact]
    public async Task PostRecommendation_UnsupportedZone_DoesNotCallProvider()
    {
        // Arrange
        var request = CreateValidRequest();
        request.Zone = "NYISO";

        // Act
        await _controller.PostRecommendation(request, CancellationToken.None);

        // Assert
        _mockProvider.Verify(
            p => p.GetHourlyAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Provider Exception Tests

    [Fact]
    public async Task PostRecommendation_ProviderThrowsException_ReturnsInternalServerError()
    {
        // Arrange
        var request = CreateValidRequest();
        _mockProvider
            .Setup(p => p.GetHourlyAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database connection failed"));

        // Act
        var result = await _controller.PostRecommendation(request, CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);

        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("Error retrieving carbon intensity data", problemDetails.Title);
        Assert.Equal("Database connection failed", problemDetails.Detail);
    }

    [Fact]
    public async Task PostRecommendation_ProviderThrowsFileNotFoundException_ReturnsInternalServerError()
    {
        // Arrange
        var request = CreateValidRequest();
        _mockProvider
            .Setup(p => p.GetHourlyAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException("CSV file not found"));

        // Act
        var result = await _controller.PostRecommendation(request, CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);

        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("Error retrieving carbon intensity data", problemDetails.Title);
        Assert.Contains("CSV file not found", problemDetails.Detail);
    }

    #endregion

    #region Empty Data Tests

    [Fact]
    public async Task PostRecommendation_ProviderReturnsEmptyList_ReturnsNotFound()
    {
        // Arrange
        var request = CreateValidRequest();
        _mockProvider
            .Setup(p => p.GetHourlyAsync(
                request.Zone,
                request.WindowStartUtc,
                request.WindowEndUtc,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CarbonIntensityPoint>());

        // Act
        var result = await _controller.PostRecommendation(request, CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);

        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("No carbon intensity data available", problemDetails.Title);
        Assert.Contains("CAISO", problemDetails.Detail);
        Assert.Contains(request.WindowStartUtc.ToString("u"), problemDetails.Detail);
        Assert.Contains(request.WindowEndUtc.ToString("u"), problemDetails.Detail);
    }

    [Fact]
    public async Task PostRecommendation_ProviderReturnsNull_ReturnsNotFound()
    {
        // Arrange
        var request = CreateValidRequest();
        _mockProvider
            .Setup(p => p.GetHourlyAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<CarbonIntensityPoint>)null!);

        // Act
        var result = await _controller.PostRecommendation(request, CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);

        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("No carbon intensity data available", problemDetails.Title);
    }

    #endregion

    #region Infeasible Request Tests

    [Fact]
    public async Task PostRecommendation_InfeasibleRequest_ReturnsUnprocessableEntity()
    {
        // Arrange
        var request = CreateValidRequest();
        request.WindowStartUtc = new DateTime(2025, 12, 15, 8, 0, 0, DateTimeKind.Utc);
        request.WindowEndUtc = new DateTime(2025, 12, 15, 9, 0, 0, DateTimeKind.Utc); // 1 hour window
        request.KWhNeeded = 50m;   // needs 5 hours at 10kW
        request.MaxChargingKw = 10m;

        var carbonData = new List<CarbonIntensityPoint>
    {
        new CarbonIntensityPoint(request.WindowStartUtc, 0.5)
    };

        _mockProvider
            .Setup(p => p.GetHourlyAsync(
                request.Zone,
                request.WindowStartUtc,
                request.WindowEndUtc,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(carbonData);

        // Act
        var result = await _controller.PostRecommendation(request, CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, objectResult.StatusCode);

        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("Infeasible charging request", problemDetails.Title);
        Assert.Contains("No feasible charging start time", problemDetails.Detail);
    }


    #endregion


    #region Helper Methods

    // Helper: configure a 1-hour charge request for a given window
    private static ChargingRecommendationRequest OneHourChargeRequest(DateTime windowStart, DateTime windowEnd)
    {
        return new ChargingRecommendationRequest
        {
            Zone = "CAISO",
            WindowStartUtc = windowStart,
            WindowEndUtc = windowEnd,
            KWhNeeded = 10m, // 1 hour at 10kW
            MaxChargingKw = 10m
        };
    }

    // Helper: generate a carbon series for a window, given intensities (one per hour)
    private static List<CarbonIntensityPoint> CarbonSeries(DateTime windowStart, params double[] intensities)
    {
        var list = new List<CarbonIntensityPoint>();
        for (int hourOffset = 0; hourOffset < intensities.Length; hourOffset++)
        {
            list.Add(new CarbonIntensityPoint(windowStart.AddHours(hourOffset), intensities[hourOffset]));
        }
        return list;
    }
    #endregion
    #region Happy Path Tests

    [Fact]
    public async Task PostRecommendation_ValidRequest_ReturnsOkWithRecommendation()
    {
        // Arrange
        var windowStart = new DateTime(2025, 12, 15, 8, 0, 0, DateTimeKind.Utc);
        var windowEnd = windowStart.AddHours(3); // 3-hour window
        var request = OneHourChargeRequest(windowStart, windowEnd);
        var carbonData = CarbonSeries(windowStart, 0.9, 0.1, 0.8); // 09:00 is best

        _mockProvider
            .Setup(p => p.GetHourlyAsync(
                request.Zone,
                request.WindowStartUtc,
                request.WindowEndUtc,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(carbonData);

        // Act
        var result = await _controller.PostRecommendation(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ChargingRecommendationResponse>(okResult.Value);

        Assert.Equal("CAISO", response.Zone);
        Assert.Equal(windowStart.AddHours(1), response.RecommendedStartUtc); // 09:00
        Assert.Equal(windowStart.AddHours(2), response.RecommendedEndUtc);   // 10:00
        Assert.True(response.EstimatedEmissionsKg > 0m);
    }

    [Fact]
    public async Task PostRecommendation_ValidRequest_IncludesAssumptions()
    {
        // Arrange
        var windowStart = new DateTime(2025, 12, 15, 8, 0, 0, DateTimeKind.Utc);
        var windowEnd = windowStart.AddHours(3);
        var request = OneHourChargeRequest(windowStart, windowEnd);
        var carbonData = CarbonSeries(windowStart, 0.9, 0.1, 0.8);

        _mockProvider
            .Setup(p => p.GetHourlyAsync(
                request.Zone,
                request.WindowStartUtc,
                request.WindowEndUtc,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(carbonData);

        // Act
        var result = await _controller.PostRecommendation(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ChargingRecommendationResponse>(okResult.Value);

        Assert.NotNull(response.Assumptions);
        Assert.True(response.Assumptions.HourlyResolution, "HourlyResolution should be true");
        Assert.Contains("DATA_NOTES.md", response.Assumptions.EmissionsFactors);
    }

    [Fact]
    public async Task PostRecommendation_ValidRequest_CallsProviderWithCorrectParameters()
    {
        // Arrange
        var windowStart = new DateTime(2025, 12, 15, 8, 0, 0, DateTimeKind.Utc);
        var windowEnd = windowStart.AddHours(3);
        var request = OneHourChargeRequest(windowStart, windowEnd);
        var carbonData = CarbonSeries(windowStart, 0.9, 0.1, 0.8);

        _mockProvider
            .Setup(p => p.GetHourlyAsync(
                request.Zone,
                request.WindowStartUtc,
                request.WindowEndUtc,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(carbonData);

        // Act
        await _controller.PostRecommendation(request, CancellationToken.None);

        // Assert
        _mockProvider.Verify(
            p => p.GetHourlyAsync(
                "CAISO",
                request.WindowStartUtc,
                request.WindowEndUtc,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PostRecommendation_ValidRequest_PassesCancellationToken()
    {
        // Arrange
        var windowStart = new DateTime(2025, 12, 15, 8, 0, 0, DateTimeKind.Utc);
        var windowEnd = windowStart.AddHours(3);
        var request = OneHourChargeRequest(windowStart, windowEnd);
        var cts = new CancellationTokenSource();
        var carbonData = CarbonSeries(windowStart, 0.5, 0.4, 0.3);

        _mockProvider
            .Setup(p => p.GetHourlyAsync(
                request.Zone,
                request.WindowStartUtc,
                request.WindowEndUtc,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(carbonData);

        // Act
        await _controller.PostRecommendation(request, cts.Token);

        // Assert - verify the cancellation token was propagated to the provider
        _mockProvider.Verify(
            p => p.GetHourlyAsync(
                request.Zone,
                request.WindowStartUtc,
                request.WindowEndUtc,
                cts.Token),
            Times.Once);
    }

    [Fact]
    public async Task PostRecommendation_ValidRequest_MapsRequestToChargingRequestCorrectly()
    {
        // Arrange - verify that the controller properly uses the request parameters
        // by checking that the response reflects the input constraints
        var windowStart = new DateTime(2025, 12, 15, 8, 0, 0, DateTimeKind.Utc);
        var windowEnd = windowStart.AddHours(10);
        var request = OneHourChargeRequest(windowStart, windowEnd);
        var intensities = new double[] { 0.9, 0.8, 0.1, 0.7, 0.5, 0.6, 0.4, 0.3, 0.2, 0.15 };
        var carbonData = CarbonSeries(windowStart, intensities);

        _mockProvider
            .Setup(p => p.GetHourlyAsync(
                request.Zone,
                request.WindowStartUtc,
                request.WindowEndUtc,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(carbonData);

        // Act
        var result = await _controller.PostRecommendation(request, CancellationToken.None);

        // Assert - verify the response contains expected values based on the request
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ChargingRecommendationResponse>(okResult.Value);

        Assert.Equal(request.Zone, response.Zone);

        // Find the index of the minimum intensity
        var minIndex = Array.IndexOf(intensities, intensities.Min());
        var expectedStart = windowStart.AddHours(minIndex);
        var expectedEnd = expectedStart.AddHours(1);

        Assert.Equal(expectedStart, response.RecommendedStartUtc);
        Assert.Equal(expectedEnd, response.RecommendedEndUtc);

        // Verify the charging duration matches the requirement (1 hour for 10kWh at 10kW)
        var duration = (response.RecommendedEndUtc - response.RecommendedStartUtc).TotalHours;
        Assert.Equal(1.0, duration, precision: 2);
    }

    [Fact]
    public async Task PostRecommendation_TieInEmissions_ReturnsEarliestStartTime()
    {
        // Arrange - create scenario where multiple start times yield equal emissions
        var windowStart = new DateTime(2025, 12, 15, 8, 0, 0, DateTimeKind.Utc);
        var windowEnd = windowStart.AddHours(4);
        var request = OneHourChargeRequest(windowStart, windowEnd);
        // 08:00, 10:00, 11:00 all have the same low intensity
        var carbonData = CarbonSeries(windowStart, 0.3, 0.9, 0.3, 0.3);

        _mockProvider
            .Setup(p => p.GetHourlyAsync(
                request.Zone,
                request.WindowStartUtc,
                request.WindowEndUtc,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(carbonData);

        // Act
        var result = await _controller.PostRecommendation(request, CancellationToken.None);

        // Assert - verify the planner chose the earliest of the tied options
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ChargingRecommendationResponse>(okResult.Value);

        Assert.Equal(windowStart, response.RecommendedStartUtc); // Should be 08:00, not 10:00 or 11:00
        Assert.Equal(windowStart.AddHours(1), response.RecommendedEndUtc); // 1-hour charge ending at 09:00
        var expectedEmissions = 0.3m * 10m; // 0.3 kg/kWh * 10 kWh
        Assert.Equal(expectedEmissions, response.EstimatedEmissionsKg);
    }
    #endregion
}