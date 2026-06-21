using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Payments.IntegrationEvents;

/// <summary>
/// <see cref="Payment"/> aggregate'inden finance projection snapshot üretir.
/// </summary>
public static class PaymentProjectionSnapshotFactory
{
    public static PaymentProjectionSnapshot Create(Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);

        return new PaymentProjectionSnapshot(
            payment.Id,
            payment.TenantId,
            payment.ClinicId,
            payment.ClientId,
            payment.PetId,
            payment.AppointmentId,
            payment.ExaminationId,
            payment.Amount,
            payment.Currency,
            (int)payment.Method,
            payment.PaidAtUtc,
            PaymentIntegrationEventTypes.SchemaVersion);
    }
}
