namespace EvCharging.API.Contracts;

public class AssumptionsDto
{
    public bool HourlyResolution { get; init; }
    public string EmissionsFactors { get; init; } = "see DATA_NOTES.md";
}
