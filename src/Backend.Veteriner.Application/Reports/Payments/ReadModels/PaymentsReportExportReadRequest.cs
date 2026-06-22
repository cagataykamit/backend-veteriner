using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Reports.Payments.ReadModels;

/// <summary>
/// Query DB <c>PaymentReadModels</c> üzerinden payment export okuma isteği (15J).
/// <para>
/// <see cref="TenantId"/> zorunludur. <see cref="ClinicId"/> opsiyoneldir: dolu ise yalnız o klinik, <c>null</c> ise
/// tenant-wide okunur. Search desteklenmez (handler yalnızca search boş iken Query path'i seçer).
/// Filtre kümesi export Command DB davranışı ile birebir aynıdır (date range + clinic + client + pet + method).
/// </para>
/// </summary>
public sealed record PaymentsReportExportReadRequest(
    Guid TenantId,
    Guid? ClinicId,
    Guid? ClientId,
    Guid? PetId,
    PaymentMethod? Method,
    DateTime FromUtc,
    DateTime ToUtc);
