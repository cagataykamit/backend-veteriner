using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hospitalizations.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Hospitalizations.Commands.Discharge;

public sealed class DischargeHospitalizationCommandHandler : IRequestHandler<DischargeHospitalizationCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Hospitalization> _hospitalizationsRead;
    private readonly IRepository<Hospitalization> _hospitalizationsWrite;

    public DischargeHospitalizationCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Tenant> tenants,
        IReadRepository<Hospitalization> hospitalizationsRead,
        IRepository<Hospitalization> hospitalizationsWrite)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _tenants = tenants;
        _hospitalizationsRead = hospitalizationsRead;
        _hospitalizationsWrite = hospitalizationsWrite;
    }

    public async Task<Result> Handle(DischargeHospitalizationCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
        {
            return Result.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için taburcu işlemi yapılamaz.");
        }

        var row = await _hospitalizationsRead.FirstOrDefaultAsync(
            new HospitalizationByIdSpec(tenantId, request.Id), ct);
        if (row is null)
            return Result.Failure("Hospitalizations.NotFound", "Yatış kaydı bulunamadı.");

        if (_clinicContext.ClinicId is { } ctxClinicId && row.ClinicId != ctxClinicId)
            return Result.Failure("Hospitalizations.NotFound", "Yatış kaydı bulunamadı.");

        var dischargedUtc = AdmittedAtUtcWindow.ToUtc(request.DischargedAtUtc);
        var applyNotes = request.Notes != null;
        var discharge = row.Discharge(dischargedUtc, applyNotes, request.Notes);
        if (!discharge.IsSuccess)
            return discharge;

        await _hospitalizationsWrite.UpdateAsync(row, ct);
        await _hospitalizationsWrite.SaveChangesAsync(ct);
        return Result.Success();
    }
}
