using Backend.Veteriner.Application.Visits.Contracts.Dtos;

namespace Backend.Veteriner.Application.Visits.ReadModels;

/// <summary>
/// Bugün yüzeyi için ham satırlar. Sıralama, sınır kontrolü ve gün hesabı handler'dadır;
/// okuyucu yalnızca verilen UTC aralığı için veri toplar.
/// </summary>
public interface IVisitTodayReader
{
    /// <summary>
    /// Her kaynak (Visit satırları, Visit'siz planlı randevular) en çok <see cref="VisitTodayReadRequest.MaxItems"/> + 1
    /// satır döner; handler sınır aşımını bu fazla satırdan anlar.
    /// </summary>
    Task<VisitTodayReadResult> GetAsync(VisitTodayReadRequest request, CancellationToken ct = default);
}

public sealed record VisitTodayReadRequest(
    Guid TenantId,
    Guid ClinicId,
    DateTime DayStartUtc,
    DateTime DayEndUtc,
    bool IncludeCarriedOver,
    int MaxItems,
    bool OnlyVoided = false);

public sealed record VisitTodayReadResult(
    IReadOnlyList<TodayItemDto> Items,
    IReadOnlyList<TodayHospitalizationDto> ActiveHospitalizations);
