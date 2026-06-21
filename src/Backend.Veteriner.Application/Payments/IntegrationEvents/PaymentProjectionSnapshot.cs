namespace Backend.Veteriner.Application.Payments.IntegrationEvents;

/// <summary>
/// Payment dashboard finance projection için denormalize anlık görüntü.
/// <see cref="SchemaVersion"/> ile event contract versiyonu deterministiktir (<c>payment.*.v1</c>).
/// Finansal toplamlar için <see cref="Amount"/>, <see cref="Currency"/> ve <see cref="PaidAtUtc"/> zorunludur.
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
    int SchemaVersion);
