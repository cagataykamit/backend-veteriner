namespace Backend.Veteriner.Application.Dashboard.ReadModels;

/// <summary>
/// Query DB <c>PaymentReadModels</c> üzerinden dashboard recent payments okuma isteği.
/// Tenant + tek klinik kapsamı zorunludur.
/// </summary>
public sealed record DashboardRecentPaymentsReadRequest(
    Guid TenantId,
    Guid ClinicId,
    int Take);
