using Backend.Veteriner.Application.Dashboard.Specs;

namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Dashboard özetinde "aktif klinik seçili" yol için Client/Pet alanlarını klinik-bazlı veriye daraltır.
/// Client ve Pet entity'lerinde <c>ClinicId</c> olmadığı için ilişki <see cref="Backend.Veteriner.Domain.Appointments.Appointment"/>
/// üzerinden kurulur: bir Pet, "bu klinikte en az bir randevusu olan" Pet; bir Client ise o tür Pet'lerin sahibidir.
/// Sıralama: en güncel randevu zamanına (<c>ScheduledAtUtc</c>) göre DESC.
/// </summary>
public interface IDashboardClinicScopedReader
{
    /// <summary>Seçili klinikte en az bir randevusu olan distinct Pet sayısı.</summary>
    Task<int> CountPetsAtClinicAsync(Guid tenantId, Guid clinicId, CancellationToken ct = default);

    /// <summary>Seçili klinikte en az bir randevusu olan Pet'lerin distinct Client sayısı.</summary>
    Task<int> CountClientsAtClinicAsync(Guid tenantId, Guid clinicId, CancellationToken ct = default);

    /// <summary>Seçili klinikteki en güncel randevu zamanına göre sıralı Pet'ler (en yeni önce).</summary>
    Task<IReadOnlyList<DashboardRecentPetRow>> ListRecentPetsAtClinicAsync(
        Guid tenantId,
        Guid clinicId,
        int take,
        CancellationToken ct = default);

    /// <summary>Seçili klinikteki Pet'lerin sahibi Client'lar; en güncel randevu zamanına göre sıralı (en yeni önce).</summary>
    Task<IReadOnlyList<DashboardRecentClientRow>> ListRecentClientsAtClinicAsync(
        Guid tenantId,
        Guid clinicId,
        int take,
        CancellationToken ct = default);
}
