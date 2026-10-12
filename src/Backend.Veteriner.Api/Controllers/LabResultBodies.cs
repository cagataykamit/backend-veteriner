namespace Backend.Veteriner.Api.Controllers;

/// <summary>PUT /api/v1/lab-results/{id} body; route id is source of truth.</summary>
public sealed class UpdateLabResultBody
{
    /// <summary>Optional; when set must match route id.</summary>
    public Guid? Id { get; init; }

    public Guid ClinicId { get; init; }
    public Guid PetId { get; init; }
    public Guid? ExaminationId { get; init; }
    public Guid? VisitId { get; init; }
    public DateTime? ResultDateUtc { get; init; }
    public string TestName { get; init; } = default!;
    public string? ResultText { get; init; }
    public string? Interpretation { get; init; }
    public string? Notes { get; init; }

    /// <summary>GET ile alınan Base64 sürüm; zorunlu.</summary>
    public string? RowVersion { get; init; }
}

/// <summary>POST /api/v1/lab-results/{id}/cancel body; gerekçe 5-500 karakter, rowVersion zorunlu.</summary>
public sealed class CancelLabResultBody
{
    public string? Reason { get; init; }
    public string? RowVersion { get; init; }
}
