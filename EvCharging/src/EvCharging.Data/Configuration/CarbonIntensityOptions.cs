namespace EvCharging.Data.Configuration;

public sealed class CarbonIntensityOptions
{
    public const string SectionName = "CarbonIntensity";

    public string DefaultZone { get; set; } = "CAISO";
    public CaisoOptions Caiso { get; set; } = new();
}

public sealed class CaisoOptions
{
    public string CsvPath { get; set; } = string.Empty;
}
