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

    /// <summary>Geliş (Visit) kimliği; verilirse hayvan/klinik/randevu Visit'ten türetilir.</summary>
    public Guid? VisitId { get; init; }
    public DateTime ExaminedAtUtc { get; init; }

    /// <summary>Başvuru nedeni (canonical).</summary>
    public string? VisitReason { get; init; }

    /// <summary>Legacy JSON adı <c>complaint</c>. Yeni istemciler <see cref="VisitReason"/> kullanmalı.</summary>
    [JsonPropertyName("complaint")]
    public string? Complaint { get; init; }

    public string? Anamnesis { get; init; }
    public string? Findings { get; init; }
    public decimal? WeightKg { get; init; }
    public decimal? TemperatureC { get; init; }
    public int? HeartRateBpm { get; init; }
    public int? RespiratoryRatePerMin { get; init; }
    public DateTime? VitalsMeasuredAtUtc { get; init; }
    public string? Assessment { get; init; }
    public string? Plan { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// PUT /examinations/{id} gövdesi. Kanonik: <see cref="VisitReason"/>; <c>complaint</c> legacy.
/// <see cref="RowVersion"/> zorunlu (GET ile alınan Base64 sürüm). Kısmi güncelleme: null = dokunma, metinde boş/boşluk = temizle; vitaller için <see cref="ClearVitals"/>.
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

    public string? Anamnesis { get; init; }
    public string? Findings { get; init; }
    public decimal? WeightKg { get; init; }
    public decimal? TemperatureC { get; init; }
    public int? HeartRateBpm { get; init; }
    public int? RespiratoryRatePerMin { get; init; }
    public DateTime? VitalsMeasuredAtUtc { get; init; }
    public string? Assessment { get; init; }
    public string? Plan { get; init; }
    public string? Notes { get; init; }

    /// <summary>true: tüm vital değerleri ve ölçüm zamanını temizler; vital değer/<c>vitalsMeasuredAtUtc</c> ile birlikte gönderilemez.</summary>
    public bool ClearVitals { get; init; }

    public string? RowVersion { get; init; }
}
