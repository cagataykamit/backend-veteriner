using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Payments.Queries.GetById;

public sealed class GetPaymentByIdQueryHandler
    : IRequestHandler<GetPaymentByIdQuery, Result<PaymentDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Payment> _payments;

    public GetPaymentByIdQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<Payment> payments)
    {
        _tenantContext = tenantContext;
        _payments = payments;
    }

    public async Task<Result<PaymentDetailDto>> Handle(GetPaymentByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PaymentDetailDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var p = await _payments.FirstOrDefaultAsync(
            new PaymentByIdSpec(tenantId, request.Id), ct);
        if (p is null)
            return Result<PaymentDetailDto>.Failure("Payments.NotFound", "Ödeme kaydı bulunamadı.");

        var dto = new PaymentDetailDto(
            p.Id,
            p.TenantId,
            p.ClinicId,
            p.ClientId,
            p.PetId,
            p.AppointmentId,
            p.ExaminationId,
            p.Amount,
            p.Currency,
            p.Method,
            p.PaidAtUtc,
            p.Notes);
        return Result<PaymentDetailDto>.Success(dto);
    }
}
