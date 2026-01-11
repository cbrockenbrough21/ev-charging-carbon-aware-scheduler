using EvCharging.Core.Domain;

namespace EvCharging.Core.Providers
{
    internal class CsvCarbonIntensityProvider : ICarbonIntensityProvider
    {
        internal CsvCarbonIntensityProvider() { }

        public Task<IReadOnlyList<CarbonIntensityPoint>> GetHourlyAsync(
            string zone,
            DateTime startUtc,
            DateTime endUtc,
            CancellationToken ct = default)
        {
            IReadOnlyList<CarbonIntensityPoint> result = Array.Empty<CarbonIntensityPoint>();
            return Task.FromResult(result);
        }
    }
}
