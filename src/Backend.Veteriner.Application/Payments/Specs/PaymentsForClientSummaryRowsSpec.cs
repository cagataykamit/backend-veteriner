using Ardalis.Specification;
using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Payments.Specs;

/// <summary>Müşteri ödeme özeti için projeksiyon satırları (tüm eşleşen ödemeler).</summary>
public sealed record ClientPaymentSummaryRow(
    Guid Id,
    DateTime PaidAtUtc,
    Guid ClinicId,
    Guid? PetId,
    decimal Amount,
    string Currency,
    PaymentMethod Method,
    string? Notes);

public sealed class PaymentsForClientSummaryRowsSpec : Specification<Payment, ClientPaymentSummaryRow>
{
    public PaymentsForClientSummaryRowsSpec(Guid tenantId, Guid? clinicId, Guid clientId)
    {
        Query.Where(p => p.TenantId == tenantId && p.ClientId == clientId);
        if (clinicId.HasValue)
            Query.Where(p => p.ClinicId == clinicId.Value);
        Query.Select(p => new ClientPaymentSummaryRow(
            p.Id,
            p.PaidAtUtc,
            p.ClinicId,
            p.PetId,
            p.Amount,
            p.Currency,
            p.Method,
            p.Notes));
    }
}
