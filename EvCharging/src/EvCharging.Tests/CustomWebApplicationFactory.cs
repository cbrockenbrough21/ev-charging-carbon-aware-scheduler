using EvCharging.API;
using EvCharging.Core.Providers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EvCharging.Tests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public Mock<ICarbonIntensityProvider> CarbonIntensityProviderMock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove registrations for the interface itself
            var descriptors = services
                .Where(d => d.ServiceType == typeof(ICarbonIntensityProvider))
                .ToList();

            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }

            // Remove registrations where the concrete type implements ICarbonIntensityProvider
            var implDescriptors = services
                .Where(d => d.ImplementationType is not null
                            && typeof(ICarbonIntensityProvider).IsAssignableFrom(d.ImplementationType))
                .ToList();

            foreach (var descriptor in implDescriptors)
            {
                services.Remove(descriptor);
            }

            // Register the mock
            services.AddSingleton<ICarbonIntensityProvider>(CarbonIntensityProviderMock.Object);
        });
    }
}
