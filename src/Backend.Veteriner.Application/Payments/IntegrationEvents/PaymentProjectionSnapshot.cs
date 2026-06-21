namespace Backend.Veteriner.Application.Payments.IntegrationEvents;

/// <summary>
/// Payment dashboard finance + list projection için denormalize anlık görüntü.
/// <see cref="SchemaVersion"/> ile event contract versiyonu deterministiktir (<c>payment.*.v1</c>).
/// Finansal toplamlar için <see cref="Amount"/>, <see cref="Currency"/> ve <see cref="PaidAtUtc"/> zorunludur.
///
/// CQRS-14C enrichment: <see cref="ClientName"/>/<see cref="PetName"/> ve normalize alanları
/// PaymentReadModel list/search yüzeyini besler. Bu alanlar geriye uyumluluk için nullable'dır:
/// 14C öncesi (eski) payload'larda bulunmazlar ve projection tarafında defensive fallback uygulanır.
/// </summary>
public sealed record PaymentProjectionSnapshot(
    Guid PaymentId,
    Guid TenantId,
    Guid ClinicId,
    Guid ClientId,
    Guid? PetId,
    Guid? AppointmentId,
    Guid? ExaminationId,
    decimal Amount,
    string Currency,
    int Method,
    DateTime PaidAtUtc,
    int SchemaVersion,
    string? ClientName = null,
    string? ClientNameNormalized = null,
    string? PetName = null,
    string? PetNameNormalized = null,
    string? Notes = null,
    string? NotesNormalized = null);
