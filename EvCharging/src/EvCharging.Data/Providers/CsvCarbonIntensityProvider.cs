using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using EvCharging.Core.Domain;
using EvCharging.Core.Providers;
using EvCharging.Data.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EvCharging.Data.Providers;

public sealed class CsvCarbonIntensityProvider : ICarbonIntensityProvider
{
    private readonly CarbonIntensityOptions _options;
    private readonly ILogger<CsvCarbonIntensityProvider> _logger;

    // Emissions factors (kg CO₂ / kWh)
    private static readonly Dictionary<string, double> EmissionsFactors = new()
    {
        ["natural_gas"] = 0.40,
        ["coal"] = 1.00,
        ["oil"] = 0.80,
        ["solar"] = 0.00,
        ["wind"] = 0.00,
        ["large_hydro"] = 0.00,
        ["small_hydro"] = 0.00,
        ["nuclear"] = 0.00,
        ["geothermal"] = 0.00,
        ["biomass"] = 0.00,
        ["biogas"] = 0.00,
        ["batteries"] = 0.00
    };

    public CsvCarbonIntensityProvider(
        IOptions<CarbonIntensityOptions> options,
        ILogger<CsvCarbonIntensityProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CarbonIntensityPoint>> GetHourlyAsync(
        string zone,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken ct = default)
    {
        // Validate zone
        if (!string.Equals(zone, "CAISO", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Zone '{zone}' is not supported. Only 'CAISO' is supported in MVP.", nameof(zone));
        }

        // Get CSV path from configuration
        var csvPath = _options.Caiso.CsvPath;
        if (string.IsNullOrWhiteSpace(csvPath))
        {
            throw new InvalidOperationException("CAISO CSV path is not configured.");
        }

        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException($"CAISO CSV file not found at: {csvPath}", csvPath);
        }

        // Parse CSV and calculate carbon intensity
        var points = new List<CarbonIntensityPoint>();
        var totalRows = 0;
        var skippedRows = 0;
        var rowsInRange = 0;
        var zeroGenerationRows = 0;

        using var reader = new StreamReader(csvPath);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            PrepareHeaderForMatch = args => args.Header.ToLowerInvariant(),
            MissingFieldFound = null // Don't throw on missing fields
        });

        await csv.ReadAsync();
        csv.ReadHeader();

        // Validate required columns
        var headerRecord = csv.HeaderRecord ?? throw new InvalidOperationException("CSV has no header row.");
        if (!headerRecord.Any(h => h.Equals("interval_start_utc", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("CSV is missing required column: interval_start_utc");
        }

        // Build headers set for efficient lookup
        var headers = new HashSet<string>(headerRecord, StringComparer.OrdinalIgnoreCase);

        while (await csv.ReadAsync())
        {
            totalRows++;
            ct.ThrowIfCancellationRequested();

            try
            {
                // Parse timestamp with offset support
                var timestampStr = csv.GetField<string>("interval_start_utc");
                if (!DateTimeOffset.TryParse(timestampStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestampOffset))
                {
                    _logger.LogWarning("Skipping row {RowNumber}: invalid timestamp '{Timestamp}'", csv.Context.Parser.Row, timestampStr);
                    skippedRows++;
                    continue;
                }

                var timestamp = timestampOffset.UtcDateTime;

                // Filter to requested range
                if (timestamp < startUtc || timestamp >= endUtc)
                {
                    continue;
                }

                // Row is within range
                rowsInRange++;

                // Calculate carbon intensity
                var (intensity, hasZeroGeneration, parseSuccess) = CalculateCarbonIntensity(csv, headers);
                
                if (!parseSuccess)
                {
                    _logger.LogWarning("Skipping row {RowNumber}: error parsing fuel mix data at {Timestamp}", 
                        csv.Context.Parser.Row, timestamp);
                    skippedRows++;
                    continue;
                }

                if (hasZeroGeneration)
                {
                    zeroGenerationRows++;
                    _logger.LogWarning("Skipping row {RowNumber}: total generation is zero at {Timestamp}", 
                        csv.Context.Parser.Row, timestamp);
                    continue;
                }

                points.Add(new CarbonIntensityPoint(timestamp, intensity));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Skipping row {RowNumber}: error parsing data", csv.Context.Parser.Row);
                skippedRows++;
            }
        }

        // Validate results
        if (totalRows == 0)
        {
            throw new InvalidOperationException("CSV file contains no data rows.");
        }

        // No rows in the requested time window is not an error - just return empty list
        if (rowsInRange == 0)
        {
            _logger.LogWarning("No data points found in requested time range {StartUtc} to {EndUtc}", startUtc, endUtc);
            return points;
        }

        // If we had rows in range but none were valid, that's an error
        if (points.Count == 0)
        {
            throw new InvalidOperationException($"All {rowsInRange} rows in requested time range had invalid timestamps or data.");
        }

        // Check zero generation threshold only against rows in range
        if (zeroGenerationRows > rowsInRange * 0.5)
        {
            throw new InvalidOperationException($"More than 50% of rows in range ({zeroGenerationRows}/{rowsInRange}) had zero total generation.");
        }

        _logger.LogInformation("Loaded {Count} carbon intensity points from {StartUtc} to {EndUtc} ({Skipped} skipped, {ZeroGen} zero-generation)",
            points.Count, startUtc, endUtc, skippedRows, zeroGenerationRows);

        return points.OrderBy(p => p.TimestampUtc).ToList();
    }

    private (double intensity, bool hasZeroGeneration, bool parseSuccess) CalculateCarbonIntensity(
        CsvReader csv, 
        HashSet<string> headers)
    {
        double totalEmissions = 0.0;
        double totalGeneration = 0.0;

        foreach (var (fuelType, emissionFactor) in EmissionsFactors)
        {
            var columnName = $"fuel_mix.{fuelType}";
            
            // Check if column exists in headers
            if (!headers.Contains(columnName))
            {
                // Missing column - treat as 0 MW
                continue;
            }

            try
            {
                // Try to get the field value
                var mw = csv.GetField<double?>(columnName);
                
                if (!mw.HasValue)
                {
                    // Null value - treat as 0 MW
                    continue;
                }

                // Clamp negative values to 0 (CAISO can have negative values for solar/batteries)
                var clampedMw = Math.Max(0.0, mw.Value);
                
                // Convert MW to kWh (MW × 1 hour × 1000)
                var energyKwh = clampedMw * 1000.0;
                
                // Add to total generation
                totalGeneration += energyKwh;
                
                // Calculate emissions for this fuel type
                var emissions = energyKwh * emissionFactor;
                totalEmissions += emissions;
            }
            catch (Exception)
            {
                // Field exists but couldn't be parsed as double - this is a row-level error
                return (0.0, false, false);
            }
        }

        // Guard against division by zero
        if (totalGeneration <= 0)
        {
            return (0.0, true, true);
        }

        // Return carbon intensity (kg CO₂ / kWh)
        return (totalEmissions / totalGeneration, false, true);
    }
}
