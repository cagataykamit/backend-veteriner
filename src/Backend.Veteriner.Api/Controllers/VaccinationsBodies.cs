using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// PUT /vaccinations/{id} istek gövdesi; route id kaynak doğruludur.
/// </summary>
public sealed class UpdateVaccinationBody
{
    /// <summary>İsteğe bağlı; doluysa route id ile aynı olmalıdır.</summary>
    public Guid? Id { get; init; }

    public Guid ClinicId { get; init; }
    public Guid PetId { get; init; }
    public Guid? ExaminationId { get; init; }
    public Guid VaccineDefinitionId { get; init; }
    public VaccinationStatus Status { get; init; }
    public DateTime? AppliedAtUtc { get; init; }
    public DateTime? DueAtUtc { get; init; }
    public string? Notes { get; init; }

    /// <summary>Uygulama yolu (RECORD-005; tam değiştirme, <c>null</c> temizler).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AdministrationRoute? Route { get; init; }

    /// <summary>Uygulama bölgesi, serbest metin (en çok 100; tam değiştirme).</summary>
    public string? Site { get; init; }
}
