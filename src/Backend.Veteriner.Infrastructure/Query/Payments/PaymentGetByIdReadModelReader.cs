using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Payments.ReadModels;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Query.Payments;

public sealed class PaymentGetByIdReadModelReader : IPaymentGetByIdReadModelReader
{
    private readonly QueryDbContext _queryDb;

    public PaymentGetByIdReadModelReader(QueryDbContext queryDb) => _queryDb = queryDb;

    public async Task<PaymentDetailDto?> GetByIdAsync(
        Guid tenantId,
        Guid paymentId,
        CancellationToken cancellationToken = default)
    {
        var row = await _queryDb.PaymentReadModels
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId && x.PaymentId == paymentId,
                cancellationToken);

        return row is null ? null : MapDetail(row);
    }

    private static PaymentDetailDto MapDetail(Persistence.Query.Models.PaymentReadModel x)
        => new(
            x.PaymentId,
            x.TenantId,
            x.ClinicId,
            x.ClientId,
            x.ClientName,
            x.PetId,
            x.PetName ?? string.Empty,
            x.AppointmentId,
            x.ExaminationId,
            x.Amount,
            x.Currency,
            (PaymentMethod)x.Method,
            x.PaidAtUtc,
            x.Notes);
}
