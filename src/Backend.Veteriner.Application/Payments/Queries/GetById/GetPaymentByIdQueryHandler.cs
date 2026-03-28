using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Payments.Queries.GetById;

public sealed class GetPaymentByIdQueryHandler
    : IRequestHandler<GetPaymentByIdQuery, Result<PaymentDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Payment> _payments;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;

    public GetPaymentByIdQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Payment> payments,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _payments = payments;
        _pets = pets;
        _clients = clients;
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
        if (_clinicContext.ClinicId is { } clinicId && p.ClinicId != clinicId)
            return Result<PaymentDetailDto>.Failure("Payments.NotFound", "Odeme kaydi bulunamadi.");

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, p.ClientId), ct);
        var clientName = client?.FullName ?? string.Empty;

        string petName = string.Empty;
        if (p.PetId is { } petId)
        {
            var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, petId), ct);
            petName = pet?.Name ?? string.Empty;
        }

        var dto = new PaymentDetailDto(
            p.Id,
            p.TenantId,
            p.ClinicId,
            p.ClientId,
            clientName,
            p.PetId,
            petName,
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
