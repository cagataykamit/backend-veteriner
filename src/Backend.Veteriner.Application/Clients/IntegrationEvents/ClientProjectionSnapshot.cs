namespace Backend.Veteriner.Application.Clients.IntegrationEvents;

/// <summary>
/// Client read-model projection için denormalize edilmiş anlık görüntü.
/// Alanlar <c>ClientReadModels</c> kolonlarıyla birebir hizalıdır; normalize değerler
/// mevcut command-side normalizer'larıyla (trim + invariant lower, <c>TurkishMobilePhone</c>) üretilir.
/// </summary>
public sealed record ClientProjectionSnapshot(
    Guid ClientId,
    Guid TenantId,
    string FullName,
    string FullNameNormalized,
    string? Email,
    string? Phone,
    string? PhoneNormalized,
    DateTime CreatedAtUtc);
