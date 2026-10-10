using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Visits;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>POST /visits gövdesi. Randevulu: <see cref="AppointmentId"/>; randevusuz: <see cref="PetId"/> (+ klinik).</summary>
public sealed class CreateVisitBody
{
    public Guid? ClinicId { get; init; }
    public Guid? PetId { get; init; }
    public Guid? AppointmentId { get; init; }
    public Guid? ResponsibleVeterinarianUserId { get; init; }
    public bool IsUrgent { get; init; }
}

/// <summary>
/// POST /visits/{id}/corrections gövdesi. <see cref="TargetCareStatus"/> ile <see cref="MarkAsMistaken"/>
/// tam olarak biri verilmelidir; <see cref="Reason"/> zorunludur.
/// </summary>
public sealed class CorrectVisitBody
{
    public string? Reason { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public VisitCareStatus? TargetCareStatus { get; init; }

    public bool MarkAsMistaken { get; init; }
}

/// <summary>POST /visits/{id}/restore gövdesi; <see cref="Reason"/> zorunludur.</summary>
/// <summary>PUT /visits/{id}/urgency gövdesi.</summary>
public sealed class SetVisitUrgencyBody
{
    public bool IsUrgent { get; init; }
}

public sealed class RestoreVisitBody
{
    public string? Reason { get; init; }
}
