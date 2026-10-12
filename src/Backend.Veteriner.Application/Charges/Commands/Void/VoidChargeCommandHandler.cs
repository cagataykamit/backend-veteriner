using Backend.Veteriner.Application.Charges.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Finance;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Charges.Commands.Void;

public sealed class VoidChargeCommandHandler : IRequestHandler<VoidChargeCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IRepository<Charge> _charges;
    private readonly TimeProvider _timeProvider;

    public VoidChargeCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IRepository<Charge> charges,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _charges = charges;
        _timeProvider = timeProvider;
    }

    public async Task<Result> Handle(VoidChargeCommand request, CancellationToken ct)
    {
        var actor = await FinanceCorrectionActor.ResolveAsync(_tenantContext, _clientContext, _tenants, ct);
        if (!actor.IsSuccess)
            return Result.Failure(actor.Error);

        var (tenantId, userId) = actor.Value;

        var charge = await _charges.FirstOrDefaultAsync(new ChargeForUpdateSpec(tenantId, request.Id), ct);
        if (charge is null
            || (_clinicContext.ClinicId is { } contextClinicId && charge.ClinicId != contextClinicId))
        {
            return Result.Failure("Charges.NotFound", "Ücret bulunamadı.");
        }

        var clinicAccess = await _clinicScopeResolver.ResolveAsync(tenantId, charge.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result.Failure(clinicAccess.Error);

        var voided = charge.Void(userId, request.Reason, _timeProvider.GetUtcNow().UtcDateTime);
        if (!voided.IsSuccess)
            return voided;

        try
        {
            await _charges.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(
                "Charges.ConcurrencyConflict",
                "Ücret eşzamanlı olarak güncellendi; yeniden okunup işlem tekrarlanmalı.");
        }

        return Result.Success();
    }
}
