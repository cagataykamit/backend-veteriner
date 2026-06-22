using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Reports.Payments.ReadModels;

/// <summary>
/// Query DB <c>PaymentReadModels</c> üzerinden payment report JSON okuma isteği (15G + 15M search).
/// <para>
/// <see cref="TenantId"/> zorunludur. <see cref="ClinicId"/> opsiyoneldir: dolu ise yalnız o klinik, <c>null</c> ise
/// tenant-wide (tüm klinikler) okunur — mevcut Command DB scope davranışı ile uyumlu.
/// Search dolu iken <see cref="SearchContainsLikePattern"/> + lookup ID filtreleri (15M) uygulanır.
/// Filtre kümesi report JSON Command DB davranışı ile birebir aynıdır.
/// </para>
/// </summary>
public sealed record PaymentsReportReadRequest(
    Guid TenantId,
    Guid? ClinicId,
    Guid? ClientId,
    Guid? PetId,
    PaymentMethod? Method,
    DateTime FromUtc,
    DateTime ToUtc,
    int Page,
    int PageSize,
    string? SearchContainsLikePattern = null,
    IReadOnlyList<Guid>? SearchMatchClientIds = null,
    IReadOnlyList<Guid>? SearchMatchPetIds = null);
