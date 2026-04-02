using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Examinations;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// POST /examinations gövdesi. Kanonik: <see cref="VisitReason"/> (JSON <c>visitReason</c>).
/// <c>complaint</c> yalnızca eski istemciler içindir; çözümleme <see cref="ExaminationVisitReasonResolver"/>.
/// </summary>
public sealed class CreateExaminationBody
{
    public Guid? ClinicId { get; init; }
    public Guid? PetId { get; init; }
    public Guid? AppointmentId { get; init; }
    public DateTime ExaminedAtUtc { get; init; }

    /// <summary>Başvuru nedeni (canonical).</summary>
    public string? VisitReason { get; init; }

    /// <summary>Legacy JSON adı <c>complaint</c>. Yeni istemciler <see cref="VisitReason"/> kullanmalı.</summary>
    [JsonPropertyName("complaint")]
    public string? Complaint { get; init; }

    public string? Findings { get; init; }
    public string? Assessment { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// PUT /examinations/{id} gövdesi. Kanonik: <see cref="VisitReason"/>; <c>complaint</c> legacy.
/// </summary>
public sealed class UpdateExaminationBody
{
    public Guid? Id { get; init; }
    public Guid? ClinicId { get; init; }
    public Guid? PetId { get; init; }
    public Guid? AppointmentId { get; init; }
    public DateTime ExaminedAtUtc { get; init; }

    public string? VisitReason { get; init; }

    [JsonPropertyName("complaint")]
    public string? Complaint { get; init; }

    public string? Findings { get; init; }
    public string? Assessment { get; init; }
    public string? Notes { get; init; }
}

