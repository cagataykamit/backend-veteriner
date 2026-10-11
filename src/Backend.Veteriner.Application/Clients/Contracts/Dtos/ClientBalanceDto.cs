using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Clients.Contracts.Dtos;

/// <summary>
/// GET /clients/{id}/balance. Ücret toplamı, tahsis edilen ve açık kalan ayrıdır; bağlanmamış (eski) ödemeler
/// <see cref="UnallocatedPayments"/> altında ayrı satırdır ve açık tutardan düşülmez. Yalnızca TRY.
/// </summary>
public sealed record ClientBalanceDto(
    Guid ClientId,
    string ClientName,
    Guid? ClinicId,
    string Currency,
    decimal ChargedAmount,
    decimal AllocatedAmount,
    decimal OutstandingAmount,
    int OpenChargeCount,
    IReadOnlyList<ClientBalancePetDto> Pets,
    ClientBalanceUnallocatedPaymentsDto UnallocatedPayments);

public sealed record ClientBalancePetDto(
    Guid? PetId,
    string? PetName,
    decimal OutstandingAmount,
    IReadOnlyList<ClientBalanceVisitDto> Visits);

public sealed record ClientBalanceVisitDto(
    Guid? VisitId,
    DateTime? VisitArrivedAtUtc,
    decimal OutstandingAmount,
    IReadOnlyList<ClientBalanceChargeDto> Charges);

public sealed record ClientBalanceChargeDto(
    Guid ChargeId,
    Guid ClinicId,
    Guid? ExaminationId,
    string Description,
    decimal Amount,
    decimal AllocatedAmount,
    decimal OutstandingAmount,
    DateTime CreatedAtUtc);

public sealed record ClientBalanceUnallocatedPaymentsDto(
    decimal TotalAmount,
    int PaymentCount,
    bool HasOtherCurrency,
    IReadOnlyList<ClientBalanceUnallocatedPaymentDto> Items);

public sealed record ClientBalanceUnallocatedPaymentDto(
    Guid PaymentId,
    Guid ClinicId,
    DateTime PaidAtUtc,
    PaymentMethod Method,
    decimal Amount,
    decimal UnallocatedAmount,
    string Currency,
    string? Notes);
