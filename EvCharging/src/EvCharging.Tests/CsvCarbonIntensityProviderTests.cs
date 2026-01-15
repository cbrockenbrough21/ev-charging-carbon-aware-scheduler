using EvCharging.Core.Providers;
using EvCharging.Data.Configuration;
using EvCharging.Data.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EvCharging.Tests;

public class CsvCarbonIntensityProviderTests
{
    private static string GetTestDataPath(string filename)
    {
        return Path.Combine(AppContext.BaseDirectory, "Data", filename);
    }

    private static CsvCarbonIntensityProvider CreateProvider(string csvFilename)
    {
        var options = Options.Create(new CarbonIntensityOptions
        {
            Caiso = new CaisoOptions
            {
                CsvPath = GetTestDataPath(csvFilename)
            }
        });
        var logger = Mock.Of<ILogger<CsvCarbonIntensityProvider>>();
        return new CsvCarbonIntensityProvider(options, logger);
    }

    #region Valid CSV Tests

    [Fact]
    public async Task GetHourlyAsync_ValidCsv_ReturnsOrderedPointsInRange()
    {
        // Arrange
        var provider = CreateProvider("caiso_valid_hourly.csv");
        var startUtc = new DateTime(2025, 12, 15, 9, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 12, 15, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var points = await provider.GetHourlyAsync("CAISO", startUtc, endUtc);

        // Assert
        Assert.NotEmpty(points);
        Assert.Equal(3, points.Count); // 09:00, 10:00, 11:00 (end is exclusive)
        
        // Verify all points are within range
        Assert.All(points, p =>
        {
            Assert.True(p.TimestampUtc >= startUtc, $"Timestamp {p.TimestampUtc} should be >= {startUtc}");
            Assert.True(p.TimestampUtc < endUtc, $"Timestamp {p.TimestampUtc} should be < {endUtc}");
        });
        
        // Verify intensity is non-negative
        Assert.All(points, p => Assert.True(p.KgCo2PerKwh >= 0, "Carbon intensity should be non-negative"));
        
        // Verify points are ordered by timestamp
        Assert.Equal(points, points.OrderBy(p => p.TimestampUtc).ToList());
    }

    [Fact]
    public async Task GetHourlyAsync_ValidCsv_EndTimeIsExclusive()
    {
        // Arrange
        var provider = CreateProvider("caiso_valid_hourly.csv");
        var startUtc = new DateTime(2025, 12, 15, 10, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 12, 15, 11, 0, 0, DateTimeKind.Utc);

        // Act
        var points = await provider.GetHourlyAsync("CAISO", startUtc, endUtc);

        // Assert
        Assert.Single(points); // Only 10:00 should be included, not 11:00
        Assert.Equal(new DateTime(2025, 12, 15, 10, 0, 0, DateTimeKind.Utc), points[0].TimestampUtc);
    }

    [Fact]
    public async Task GetHourlyAsync_ValidCsv_ReturnsAllRowsWhenWholeFileInRange()
    {
        // Arrange
        var provider = CreateProvider("caiso_valid_hourly.csv");
        var startUtc = new DateTime(2025, 12, 15, 0, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 12, 16, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var points = await provider.GetHourlyAsync("CAISO", startUtc, endUtc);

        // Assert
        Assert.Equal(6, points.Count); // All 6 rows from CSV
    }

    #endregion

    #region Bad Rows Tests

    [Fact]
    public async Task GetHourlyAsync_BadRows_SkipsBadRowsAndReturnsValidOnes()
    {
        // Arrange
        var provider = CreateProvider("caiso_bad_rows.csv");
        var startUtc = new DateTime(2025, 12, 15, 10, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 12, 15, 14, 0, 0, DateTimeKind.Utc);

        // Act
        var points = await provider.GetHourlyAsync("CAISO", startUtc, endUtc);

        // Assert
        // Should return 2 points: 10:00 (valid) and 11:00 (negative clamped to 0, still valid if other fuels > 0)
        // 12:00 is zero generation (skipped), 13:00 is valid
        Assert.NotEmpty(points);
        Assert.True(points.Count >= 2, "Should have at least 2 valid points after skipping bad rows");
        
        // All returned points should have non-negative intensity
        Assert.All(points, p => Assert.True(p.KgCo2PerKwh >= 0));
    }

    [Fact]
    public async Task GetHourlyAsync_NegativeMwValues_ClampedToZero()
    {
        // Arrange
        var provider = CreateProvider("caiso_bad_rows.csv");
        var startUtc = new DateTime(2025, 12, 15, 11, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 12, 15, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var points = await provider.GetHourlyAsync("CAISO", startUtc, endUtc);

        // Assert
        // Row at 11:00 has negative solar value, should be clamped to 0 and still produce valid point
        Assert.Single(points);
        Assert.True(points[0].KgCo2PerKwh >= 0, "Negative values should be clamped, resulting in valid intensity");
    }

    [Fact]
    public async Task GetHourlyAsync_UnparseableFuelValue_SkipsRow()
    {
        // Arrange
        var provider = CreateProvider("caiso_unparseable_fuel.csv");
        var startUtc = new DateTime(2025, 12, 15, 10, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 12, 15, 13, 0, 0, DateTimeKind.Utc);

        // Act
        var points = await provider.GetHourlyAsync("CAISO", startUtc, endUtc);

        // Assert
        // Should return 2 points (10:00 and 12:00), skipping 11:00 with unparseable value
        Assert.Equal(2, points.Count);
        Assert.DoesNotContain(points, p => p.TimestampUtc == new DateTime(2025, 12, 15, 11, 0, 0, DateTimeKind.Utc));
    }

    #endregion

    #region Missing Columns Tests

    [Fact]
    public async Task GetHourlyAsync_MissingRequiredHeader_ThrowsInvalidOperationException()
    {
        // Arrange
        var provider = CreateProvider("caiso_missing_header.csv");
        var startUtc = new DateTime(2025, 12, 15, 10, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 12, 15, 11, 0, 0, DateTimeKind.Utc);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetHourlyAsync("CAISO", startUtc, endUtc));
        
        Assert.Contains("interval_start_utc", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHourlyAsync_MissingFuelColumns_TreatsAsMissingFuelAsZero()
    {
        // Arrange
        var provider = CreateProvider("caiso_missing_fuel_columns.csv");
        var startUtc = new DateTime(2025, 12, 15, 10, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 12, 15, 13, 0, 0, DateTimeKind.Utc);

        // Act
        var points = await provider.GetHourlyAsync("CAISO", startUtc, endUtc);

        // Assert
        // Should still return points; missing fuel columns treated as 0 MW
        Assert.Equal(3, points.Count);
        Assert.All(points, p => Assert.True(p.KgCo2PerKwh >= 0));
    }

    #endregion

    #region Configuration and File Tests

    [Fact]
    public async Task GetHourlyAsync_UnsupportedZone_ThrowsArgumentException()
    {
        // Arrange
        var provider = CreateProvider("caiso_valid_hourly.csv");
        var startUtc = new DateTime(2025, 12, 15, 10, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 12, 15, 11, 0, 0, DateTimeKind.Utc);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => provider.GetHourlyAsync("ERCOT", startUtc, endUtc));
        
        Assert.Contains("ERCOT", exception.Message);
        Assert.Contains("CAISO", exception.Message);
    }

    [Fact]
    public async Task GetHourlyAsync_MissingCsvFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var provider = CreateProvider("nonexistent_file.csv");
        var startUtc = new DateTime(2025, 12, 15, 10, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 12, 15, 11, 0, 0, DateTimeKind.Utc);

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => provider.GetHourlyAsync("CAISO", startUtc, endUtc));
    }

    [Fact]
    public async Task GetHourlyAsync_EmptyPath_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = Options.Create(new CarbonIntensityOptions
        {
            Caiso = new CaisoOptions { CsvPath = "" }
        });
        var logger = Mock.Of<ILogger<CsvCarbonIntensityProvider>>();
        var provider = new CsvCarbonIntensityProvider(options, logger);
        var startUtc = new DateTime(2025, 12, 15, 10, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 12, 15, 11, 0, 0, DateTimeKind.Utc);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetHourlyAsync("CAISO", startUtc, endUtc));
        
        Assert.Contains("not configured", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Time Range Tests

    [Fact]
    public async Task GetHourlyAsync_NoDataInRange_ReturnsEmptyList()
    {
        // Arrange
        var provider = CreateProvider("caiso_valid_hourly.csv");
        // CSV has data for Dec 15, request data for Dec 1
        var startUtc = new DateTime(2025, 12, 1, 10, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 12, 1, 11, 0, 0, DateTimeKind.Utc);

        // Act
        var points = await provider.GetHourlyAsync("CAISO", startUtc, endUtc);

        // Assert
        Assert.Empty(points);
    }

    [Fact]
    public async Task GetHourlyAsync_AllRowsInRangeInvalid_ThrowsInvalidOperationException()
    {
        // Arrange
        var provider = CreateProvider("caiso_all_invalid_in_range.csv");
        // Request range 10:00-13:00 where all rows are zero generation
        var startUtc = new DateTime(2025, 12, 15, 10, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 12, 15, 13, 0, 0, DateTimeKind.Utc);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetHourlyAsync("CAISO", startUtc, endUtc));
        
        Assert.Contains("rows in requested time range", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Zero Generation Tests

    [Fact]
    public async Task GetHourlyAsync_MoreThan50PercentZeroGeneration_ThrowsInvalidOperationException()
    {
        // Arrange
        var provider = CreateProvider("caiso_mostly_zero_generation.csv");
        // Request range that includes mostly zero generation rows
        var startUtc = new DateTime(2025, 12, 15, 10, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 12, 15, 17, 0, 0, DateTimeKind.Utc);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetHourlyAsync("CAISO", startUtc, endUtc));
        
        Assert.Contains("50%", exception.Message);
        Assert.Contains("zero total generation", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHourlyAsync_ZeroGenerationRows_SkippedWithValidRowsReturned()
    {
        // Arrange
        var provider = CreateProvider("caiso_bad_rows.csv");
        // Row at 12:00 has zero generation, should be skipped
        var startUtc = new DateTime(2025, 12, 15, 10, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 12, 15, 14, 0, 0, DateTimeKind.Utc);

        // Act
        var points = await provider.GetHourlyAsync("CAISO", startUtc, endUtc);

        // Assert
        Assert.NotEmpty(points);
        // Should not contain the 12:00 timestamp (zero generation)
        Assert.DoesNotContain(points, p => p.TimestampUtc == new DateTime(2025, 12, 15, 12, 0, 0, DateTimeKind.Utc));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task GetHourlyAsync_EmptyCsv_ThrowsInvalidOperationException()
    {
        // Arrange
        var provider = CreateProvider("caiso_empty.csv");
        var startUtc = new DateTime(2025, 12, 15, 10, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 12, 15, 11, 0, 0, DateTimeKind.Utc);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetHourlyAsync("CAISO", startUtc, endUtc));
        
        Assert.Contains("no data rows", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHourlyAsync_CaseInsensitiveZone_Succeeds()
    {
        // Arrange
        var provider = CreateProvider("caiso_valid_hourly.csv");
        var startUtc = new DateTime(2025, 12, 15, 10, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 12, 15, 11, 0, 0, DateTimeKind.Utc);

        // Act
        var pointsLower = await provider.GetHourlyAsync("caiso", startUtc, endUtc);
        var pointsUpper = await provider.GetHourlyAsync("CAISO", startUtc, endUtc);
        var pointsMixed = await provider.GetHourlyAsync("CaIsO", startUtc, endUtc);

        // Assert
        Assert.Single(pointsLower);
        Assert.Single(pointsUpper);
        Assert.Single(pointsMixed);
    }

    #endregion
}
