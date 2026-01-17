using System.ComponentModel.DataAnnotations;

namespace EvCharging.API.Contracts;

public class ChargingRecommendationRequest : IValidatableObject
{

    public string Zone { get; set; } = "CAISO";

    [Required]
    public DateTime WindowStartUtc { get; set; }

    [Required]
    public DateTime WindowEndUtc { get; set; }

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "kWhNeeded must be greater than 0")]
    public decimal KWhNeeded { get; set; }

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "maxChargingKw must be greater than 0")]
    public decimal MaxChargingKw { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (WindowStartUtc.Kind != DateTimeKind.Utc)
        {
            yield return new ValidationResult(
                "WindowStartUtc must be UTC (DateTimeKind.Utc)",
                new[] { nameof(WindowStartUtc) });
        }

        if (WindowEndUtc.Kind != DateTimeKind.Utc)
        {
            yield return new ValidationResult(
                "WindowEndUtc must be UTC (DateTimeKind.Utc)",
                new[] { nameof(WindowEndUtc) });
        }

        if (WindowEndUtc <= WindowStartUtc)
        {
            yield return new ValidationResult(
                "WindowEndUtc must be after WindowStartUtc",
                new[] { nameof(WindowEndUtc) });
        }
    }
}
