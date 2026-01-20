using EvCharging.API.Contracts;
using EvCharging.Core.Domain;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Net;
using System.Net.Http.Json;

namespace EvCharging.Tests;

public class ChargingSessionsIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ChargingSessionsIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private const string ApiEndpoint = "/v1/charging-sessions/recommendation";

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

    private static List<CarbonIntensityPoint> CreateCarbonDataWithMinAtHour(DateTime windowStart, int hours, int minAtHourOffset)
    {
        var data = new List<CarbonIntensityPoint>();
        for (int i = 0; i < hours; i++)
        {
            // Create decreasing intensity up to minAtHourOffset, then low values after
            // This ensures starting at minAtHourOffset is actually optimal
            var intensity = i < minAtHourOffset ? 0.9 - (i * 0.05) : 0.1;
            data.Add(new CarbonIntensityPoint(windowStart.AddHours(i), intensity));
        }
        return data;
    }

    #region Happy Path Tests

    [Fact]
    public async Task PostRecommendation_ValidRequest_ReturnsOkWithRecommendation()
    {
        // Arrange
        var request = CreateValidRequest();
        var carbonData = CreateCarbonDataWithMinAtHour(request.WindowStartUtc, 10, 3);

        _factory.CarbonIntensityProviderMock.Reset();
        _factory.CarbonIntensityProviderMock
            .Setup(p => p.GetHourlyAsync(
                request.Zone,
                request.WindowStartUtc,
                request.WindowEndUtc,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(carbonData);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ChargingRecommendationResponse>();
        Assert.NotNull(result);
        Assert.Equal("CAISO", result.Zone);
        
        // The minimum carbon intensity is at hour 3 (index 3)
        var expectedStart = request.WindowStartUtc.AddHours(3);
        Assert.Equal(expectedStart, result.RecommendedStartUtc);
        
        // 50 kWh at 10 kW = 5 hours
        var expectedEnd = expectedStart.AddHours(5);
        Assert.Equal(expectedEnd, result.RecommendedEndUtc);
        
        Assert.True(result.EstimatedEmissionsKg > 0);
        Assert.NotNull(result.Assumptions);
    }

    [Fact]
    public async Task PostRecommendation_TieInEmissions_ReturnsEarliestStartTime()
    {
        // Arrange
        var request = new ChargingRecommendationRequest
        {
            Zone = "CAISO",
            WindowStartUtc = new DateTime(2025, 12, 15, 8, 0, 0, DateTimeKind.Utc),
            WindowEndUtc = new DateTime(2025, 12, 15, 12, 0, 0, DateTimeKind.Utc),
            KWhNeeded = 10m,
            MaxChargingKw = 10m
        };

        // Create data where hours 0, 2, and 3 all have the same low intensity
        var carbonData = new List<CarbonIntensityPoint>
        {
            new CarbonIntensityPoint(request.WindowStartUtc, 0.3),
            new CarbonIntensityPoint(request.WindowStartUtc.AddHours(1), 0.9),
            new CarbonIntensityPoint(request.WindowStartUtc.AddHours(2), 0.3),
            new CarbonIntensityPoint(request.WindowStartUtc.AddHours(3), 0.3)
        };

        _factory.CarbonIntensityProviderMock.Reset();
        _factory.CarbonIntensityProviderMock
            .Setup(p => p.GetHourlyAsync(
                request.Zone,
                request.WindowStartUtc,
                request.WindowEndUtc,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(carbonData);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ChargingRecommendationResponse>();
        Assert.NotNull(result);
        
        // Should return the earliest start time (hour 0)
        Assert.Equal(request.WindowStartUtc, result.RecommendedStartUtc);
        Assert.Equal(request.WindowStartUtc.AddHours(1), result.RecommendedEndUtc);
    }

    #endregion

    #region Unsupported Zone Tests

    [Fact]
    public async Task PostRecommendation_UnsupportedZone_ReturnsBadRequestWithProblemDetails()
    {
        // Arrange
        var request = CreateValidRequest();
        request.Zone = "NYISO";

        _factory.CarbonIntensityProviderMock.Reset();

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal("Unsupported zone", problemDetails.Title);
        Assert.Equal(400, problemDetails.Status);
    }

    [Fact]
    public async Task PostRecommendation_UnsupportedZone_DoesNotCallProvider()
    {
        // Arrange
        var request = CreateValidRequest();
        request.Zone = "ERCOT";

        _factory.CarbonIntensityProviderMock.Reset();

        // Act
        await _client.PostAsJsonAsync(ApiEndpoint, request, CancellationToken.None);

        // Assert
        _factory.CarbonIntensityProviderMock.Verify(
            p => p.GetHourlyAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Invalid Window Tests

    [Fact]
    public async Task PostRecommendation_WindowEndBeforeStart_ReturnsBadRequestWithProblemDetails()
    {
        // Arrange
        var request = CreateValidRequest();
        request.WindowStartUtc = new DateTime(2025, 12, 15, 18, 0, 0, DateTimeKind.Utc);
        request.WindowEndUtc = new DateTime(2025, 12, 15, 8, 0, 0, DateTimeKind.Utc);

        _factory.CarbonIntensityProviderMock.Reset();

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(400, problemDetails.Status);
    }

    #endregion

    #region Invalid KWhNeeded Tests

    [Fact]
    public async Task PostRecommendation_KWhNeededZero_ReturnsBadRequestWithProblemDetails()
    {
        // Arrange
        var request = CreateValidRequest();
        request.KWhNeeded = 0m;

        _factory.CarbonIntensityProviderMock.Reset();

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(400, problemDetails.Status);
    }

    [Fact]
    public async Task PostRecommendation_KWhNeededNegative_ReturnsBadRequestWithProblemDetails()
    {
        // Arrange
        var request = CreateValidRequest();
        request.KWhNeeded = -10m;

        _factory.CarbonIntensityProviderMock.Reset();

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(400, problemDetails.Status);
    }

    #endregion

    #region Invalid MaxChargingKw Tests

    [Fact]
    public async Task PostRecommendation_MaxChargingKwZero_ReturnsBadRequestWithProblemDetails()
    {
        // Arrange
        var request = CreateValidRequest();
        request.MaxChargingKw = 0m;

        _factory.CarbonIntensityProviderMock.Reset();

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(400, problemDetails.Status);
    }

    [Fact]
    public async Task PostRecommendation_MaxChargingKwNegative_ReturnsBadRequestWithProblemDetails()
    {
        // Arrange
        var request = CreateValidRequest();
        request.MaxChargingKw = -5m;

        _factory.CarbonIntensityProviderMock.Reset();

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(400, problemDetails.Status);
    }

    #endregion

    #region Infeasible Charging Window Tests

    [Fact]
    public async Task PostRecommendation_NotEnoughTimeInWindow_ReturnsUnprocessableEntityWithProblemDetails()
    {
        // Arrange
        var request = new ChargingRecommendationRequest
        {
            Zone = "CAISO",
            WindowStartUtc = new DateTime(2025, 12, 15, 8, 0, 0, DateTimeKind.Utc),
            WindowEndUtc = new DateTime(2025, 12, 15, 10, 0, 0, DateTimeKind.Utc), // 2 hours
            KWhNeeded = 50m,
            MaxChargingKw = 10m // Requires 5 hours
        };

        // Provide carbon data covering the 2-hour window
        var carbonData = new List<CarbonIntensityPoint>
        {
            new CarbonIntensityPoint(request.WindowStartUtc, 0.3),
            new CarbonIntensityPoint(request.WindowStartUtc.AddHours(1), 0.3)
        };

        _factory.CarbonIntensityProviderMock.Reset();
        _factory.CarbonIntensityProviderMock
            .Setup(p => p.GetHourlyAsync(
                request.Zone,
                request.WindowStartUtc,
                request.WindowEndUtc,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(carbonData);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(422, problemDetails.Status);
        Assert.Equal("Infeasible charging request", problemDetails.Title);
    }

    #endregion

    #region No Carbon Data Tests

    [Fact]
    public async Task PostRecommendation_ProviderReturnsNoData_ReturnsNotFoundWithProblemDetails()
    {
        // Arrange
        var request = CreateValidRequest();

        _factory.CarbonIntensityProviderMock.Reset();
        _factory.CarbonIntensityProviderMock
            .Setup(p => p.GetHourlyAsync(
                request.Zone,
                request.WindowStartUtc,
                request.WindowEndUtc,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CarbonIntensityPoint>());

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(404, problemDetails.Status);
        Assert.Equal("No carbon intensity data available", problemDetails.Title);
    }

    [Fact]
    public async Task PostRecommendation_ProviderReturnsNull_ReturnsNotFoundWithProblemDetails()
    {
        // Arrange
        var request = CreateValidRequest();

        _factory.CarbonIntensityProviderMock.Reset();
        _factory.CarbonIntensityProviderMock
            .Setup(p => p.GetHourlyAsync(
                request.Zone,
                request.WindowStartUtc,
                request.WindowEndUtc,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<CarbonIntensityPoint>)null!);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(404, problemDetails.Status);
        Assert.Equal("No carbon intensity data available", problemDetails.Title);
    }

    #endregion

    #region Partial Carbon Data Coverage Tests

    [Fact]
    public async Task PostRecommendation_ProviderReturnsPartialCoverage_ReturnsUnprocessableEntityWithProblemDetails()
    {
        // Arrange
        var request = CreateValidRequest(); // 10-hour window

        // Provide only 3 hours of data - insufficient for 5-hour charging window
        var carbonData = new List<CarbonIntensityPoint>
        {
            new CarbonIntensityPoint(request.WindowStartUtc, 0.3),
            new CarbonIntensityPoint(request.WindowStartUtc.AddHours(1), 0.3),
            new CarbonIntensityPoint(request.WindowStartUtc.AddHours(2), 0.3)
        };

        _factory.CarbonIntensityProviderMock.Reset();
        _factory.CarbonIntensityProviderMock
            .Setup(p => p.GetHourlyAsync(
                request.Zone,
                request.WindowStartUtc,
                request.WindowEndUtc,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(carbonData);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(422, problemDetails.Status);
        Assert.Equal("Infeasible charging request", problemDetails.Title);
    }

    #endregion
}
