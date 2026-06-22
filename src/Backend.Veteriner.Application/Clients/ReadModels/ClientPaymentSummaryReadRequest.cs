namespace Backend.Veteriner.Application.Clients.ReadModels;

/// <summary>
/// Query DB <c>PaymentReadModels</c> üzerinden client payment summary okuma isteği (15E).
/// <para>
/// <see cref="TenantId"/> + <see cref="ClientId"/> zorunludur. <see cref="ClinicId"/> opsiyoneldir:
/// dolu ise yalnız o klinik, <c>null</c> ise tenant-wide (tüm klinikler) okunur — mevcut Command DB
/// (<c>IClinicContext.ClinicId</c>) davranışı ile uyumlu.
/// </para>
/// </summary>
public sealed record ClientPaymentSummaryReadRequest(
    Guid TenantId,
    Guid ClientId,
    Guid? ClinicId,
    int RecentTake);
