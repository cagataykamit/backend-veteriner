using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// PUT /api/v1/payments/{id} gövdesi; kaynak id route’tadır.
/// Zorunlu: <see cref="ClinicId"/>, <see cref="ClientId"/>, <see cref="Amount"/>, <see cref="Currency"/>, <see cref="Method"/>, <see cref="PaidAtUtc"/>.
/// Opsiyonel: <see cref="Id"/> (body-route eşleşmesi), <see cref="PetId"/>, <see cref="AppointmentId"/>, <see cref="ExaminationId"/>, <see cref="Notes"/>.
/// OpenAPI: <c>PaymentsContractSchemaFilter</c>.
/// </summary>
public sealed class UpdatePaymentBody
{
    /// <summary>İsteğe bağlı; doluysa route id ile aynı olmalıdır.</summary>
    public Guid? Id { get; init; }

    public Guid ClinicId { get; init; }
    public Guid ClientId { get; init; }
    public Guid? PetId { get; init; }
    public Guid? AppointmentId { get; init; }
    public Guid? ExaminationId { get; init; }
    public decimal Amount { get; init; }
    /// <summary>ISO 4217 alpha-3 (örn. TRY); zorunlu.</summary>
    public string Currency { get; init; } = default!;
    public PaymentMethod Method { get; init; }
    public DateTime PaidAtUtc { get; init; }
    public string? Notes { get; init; }
}
