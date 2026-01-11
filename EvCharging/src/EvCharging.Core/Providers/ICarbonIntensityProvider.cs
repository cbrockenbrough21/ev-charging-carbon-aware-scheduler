using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EvCharging.Core.Domain;

namespace EvCharging.Core.Providers
{
    public interface ICarbonIntensityProvider
    {
        Task<IReadOnlyList<CarbonIntensityPoint>> GetHourlyAsync(
            string zone,
            DateTime startUtc,
            DateTime endUtc,
            CancellationToken ct = default);
    }
}
