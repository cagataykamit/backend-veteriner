using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Reports.Payments.ReadModels;

/// <summary>
/// Query DB <c>PaymentReadModels</c> üzerinden payment export okuma isteği (15J + 15N search).
/// <para>
/// <see cref="TenantId"/> zorunludur. <see cref="ClinicId"/> opsiyoneldir: dolu ise yalnız o klinik, <c>null</c> ise
/// tenant-wide okunur. Search opsiyoneldir: pattern + lookup ID filtreleri (15M/15L ile aynı OR mantığı).
/// Filtre kümesi export Command DB davranışı ile birebir aynıdır (date range + clinic + client + pet + method + search).
/// </para>
/// </summary>
public sealed record PaymentsReportExportReadRequest(
    Guid TenantId,
    Guid? ClinicId,
    Guid? ClientId,
    Guid? PetId,
    PaymentMethod? Method,
    DateTime FromUtc,
    DateTime ToUtc,
    string? SearchContainsLikePattern = null,
    IReadOnlyList<Guid>? SearchMatchClientIds = null,
    IReadOnlyList<Guid>? SearchMatchPetIds = null);
