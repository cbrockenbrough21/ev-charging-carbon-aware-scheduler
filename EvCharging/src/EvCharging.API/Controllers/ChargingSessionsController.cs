using EvCharging.API.Contracts;
using EvCharging.Core.Domain;
using EvCharging.Core.Planner;
using EvCharging.Core.Providers;
using Microsoft.AspNetCore.Mvc;

namespace EvCharging.API.Controllers
{
    [ApiController]
    [Route("v1/charging-sessions")]
    public class ChargingSessionsController : ControllerBase
    {
        private readonly ICarbonIntensityProvider _carbonIntensityProvider;
        private readonly ChargingPlanner _chargingPlanner;

        private const string SupportedZone = "CAISO";

        public ChargingSessionsController(
            ICarbonIntensityProvider carbonIntensityProvider,
            ChargingPlanner chargingPlanner)
        {
            _carbonIntensityProvider = carbonIntensityProvider;
            _chargingPlanner = chargingPlanner;
        }

        [HttpPost("recommendation")]
        public async Task<IActionResult> PostRecommendation(
            [FromBody] ChargingRecommendationRequest request,
            CancellationToken ct)
        {
            // Validate Zone - only CAISO is supported
            if (request.Zone != SupportedZone)
            {
                return Problem(
                    title: "Unsupported zone",
                    detail: $"Only '{SupportedZone}' zone is supported in MVP.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            // Model validation handles:
            // - WindowEndUtc > WindowStartUtc
            // - WindowStartUtc.Kind == DateTimeKind.Utc
            // - WindowEndUtc.Kind == DateTimeKind.Utc
            // via IValidatableObject implementation

            // Get hourly carbon intensity data
            IReadOnlyList<CarbonIntensityPoint> carbonData;
            try
            {
                carbonData = await _carbonIntensityProvider.GetHourlyAsync(
                    request.Zone,
                    request.WindowStartUtc,
                    request.WindowEndUtc,
                    ct);
            }
            catch (Exception ex)
            {
                return Problem(
                    title: "Error retrieving carbon intensity data",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            // If no data available, return 404
            if (carbonData == null || carbonData.Count == 0)
            {
                return Problem(
                    title: "No carbon intensity data available",
                    detail: $"No data found for zone '{request.Zone}' between {request.WindowStartUtc:u} and {request.WindowEndUtc:u}.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            // Map API request to Core domain
            var chargingRequest = new ChargingRequest(
                Zone: request.Zone,
                WindowStartUtc: request.WindowStartUtc,
                WindowEndUtc: request.WindowEndUtc,
                KWhNeeded: (double)request.KWhNeeded,
                MaxChargingKw: (double)request.MaxChargingKw);

            // Call planner to compute best charging window
            ChargingRecommendation recommendation;
            try
            {
                recommendation = _chargingPlanner.Recommend(chargingRequest, carbonData);
            }
            catch (InvalidOperationException ex)
            {
                // Infeasible - not enough time in window
                return Problem(
                    title: "Infeasible charging request",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            // Return 200 with response
            var response = new ChargingRecommendationResponse
            {
                Zone = recommendation.Zone,
                RecommendedStartUtc = recommendation.RecommendedStartUtc,
                RecommendedEndUtc = recommendation.RecommendedEndUtc,
                EstimatedEmissionsKg = (decimal)recommendation.EstimatedEmissionsKg,
                Assumptions = new AssumptionsDto
                {
                    HourlyResolution = true,
                    EmissionsFactors = "see DATA_NOTES.md"
                }
            };

            return Ok(response);
        }
    }
}