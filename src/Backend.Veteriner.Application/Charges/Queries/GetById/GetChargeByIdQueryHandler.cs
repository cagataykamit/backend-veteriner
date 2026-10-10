using Backend.Veteriner.Application.Charges.Contracts.Dtos;
using Backend.Veteriner.Application.Charges.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Charges.Queries.GetById;

public sealed class GetChargeByIdQueryHandler : IRequestHandler<GetChargeByIdQuery, Result<ChargeDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Charge> _charges;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;

    public GetChargeByIdQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Charge> charges,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _charges = charges;
        _clients = clients;
        _pets = pets;
    }

    public async Task<Result<ChargeDto>> Handle(GetChargeByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ChargeDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var charge = await _charges.FirstOrDefaultAsync(new ChargeByIdSpec(tenantId, request.Id), ct);
        if (charge is null
            || (_clinicContext.ClinicId is { } contextClinicId && charge.ClinicId != contextClinicId))
        {
            return NotFound();
        }

        // Atanmamış klinikteki kayıt varlığı sızdırmaz: NotFound.
        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, charge.ClinicId, ct);
        if (!scope.IsSuccess)
            return NotFound();

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, charge.ClientId), ct);
        var pet = charge.PetId is { } petId
            ? await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, petId), ct)
            : null;

        return Result<ChargeDto>.Success(charge.ToDto(client?.FullName ?? string.Empty, pet?.Name));
    }

    private static Result<ChargeDto> NotFound()
        => Result<ChargeDto>.Failure("Charges.NotFound", "Ücret kaydı bulunamadı.");
}
