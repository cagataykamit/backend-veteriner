using System.Text.Json.Serialization;

namespace Backend.Veteriner.Api.Controllers;

public sealed class CreateExaminationBody
{
    public Guid? ClinicId { get; init; }
    public Guid? PetId { get; init; }
    public Guid? AppointmentId { get; init; }
    public DateTime ExaminedAtUtc { get; init; }

    // Frontend bazı ekranlarda "complaint" adıyla gönderebiliyor; ikisini de destekle.
    public string? VisitReason { get; init; }

    [JsonPropertyName("complaint")]
    public string? Complaint { get; init; }

    public string? Findings { get; init; }
    public string? Assessment { get; init; }
    public string? Notes { get; init; }
}

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

