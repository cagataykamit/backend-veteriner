namespace Backend.Veteriner.Api.Controllers;

/// <summary>PUT /api/v1/vital-measurements/{id} body; route id is source of truth. Patient, clinic and links do not change.</summary>
public sealed class UpdateVitalMeasurementBody
{
    /// <summary>Optional; when set must match route id.</summary>
    public Guid? Id { get; init; }

    public DateTime MeasuredAtUtc { get; init; }
    public decimal? TemperatureC { get; init; }
    public int? HeartRateBpm { get; init; }
    public int? RespiratoryRatePerMin { get; init; }
    public decimal? WeightKg { get; init; }
    public string? Note { get; init; }

    /// <summary>Base64 version from GET; required (ADR-043 K5).</summary>
    public string? RowVersion { get; init; }
}

/// <summary>POST /api/v1/vital-measurements/{id}/cancel body; reason 5-500 characters, rowVersion required.</summary>
public sealed class CancelVitalMeasurementBody
{
    public string? Reason { get; init; }
    public string? RowVersion { get; init; }
}
