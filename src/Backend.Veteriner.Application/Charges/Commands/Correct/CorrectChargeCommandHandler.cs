using Backend.Veteriner.Application.Charges.Contracts.Dtos;
using Backend.Veteriner.Application.Charges.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Finance;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Charges.Commands.Correct;

public sealed class CorrectChargeCommandHandler : IRequestHandler<CorrectChargeCommand, Result<ChargeDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IRepository<Charge> _charges;

    public CorrectChargeCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IRepository<Charge> charges)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _clients = clients;
        _pets = pets;
        _charges = charges;
    }

    public async Task<Result<ChargeDto>> Handle(CorrectChargeCommand request, CancellationToken ct)
    {
        var actor = await FinanceCorrectionActor.ResolveAsync(_tenantContext, _clientContext, _tenants, ct);
        if (!actor.IsSuccess)
            return Result<ChargeDto>.Failure(actor.Error);

        var (tenantId, _) = actor.Value;

        var charge = await _charges.FirstOrDefaultAsync(new ChargeForUpdateSpec(tenantId, request.Id), ct);
        if (charge is null
            || (_clinicContext.ClinicId is { } contextClinicId && charge.ClinicId != contextClinicId))
        {
            return Result<ChargeDto>.Failure("Charges.NotFound", "Ücret bulunamadı.");
        }

        var clinicAccess = await _clinicScopeResolver.ResolveAsync(tenantId, charge.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<ChargeDto>.Failure(clinicAccess.Error);

        var correction = charge.Correct(request.Description, request.Quantity, request.UnitPrice, request.Reason);
        if (!correction.IsSuccess)
            return Result<ChargeDto>.Failure(correction.Error);

        try
        {
            await _charges.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ChargeDto>.Failure(
                "Charges.ConcurrencyConflict",
                "Ücret eşzamanlı olarak güncellendi; yeniden okunup işlem tekrarlanmalı.");
        }

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, charge.ClientId), ct);
        var petName = charge.PetId is { } petId
            ? (await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, petId), ct))?.Name
            : null;

        return Result<ChargeDto>.Success(charge.ToDto(client?.FullName ?? string.Empty, petName));
    }
}
