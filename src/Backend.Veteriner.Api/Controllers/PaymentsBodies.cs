using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// PUT /payments/{id} istek gövdesi; route id kaynak doğruludur.
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
    public string Currency { get; init; } = default!;
    public PaymentMethod Method { get; init; }
    public DateTime PaidAtUtc { get; init; }
    public string? Notes { get; init; }
}
