using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Prescriptions.Access;
using Backend.Veteriner.Application.Prescriptions.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;

namespace Backend.Veteriner.Application.Prescriptions.Commands.Cancel;

/// <summary>
/// POST /api/v1/prescriptions/{id}/cancel (RECORD-008): reçete gerekçeyle iptal edilir, silinmez. Kesinleşmiş muayeneye bağlı reçetede de
/// yapılabilir (düzeltme yolu: iptal + yeni reçete, ADR-013); bu yüzden <c>ExaminationLinkedRecordLock</c> çağrılmaz.
/// </summary>
public sealed record CancelPrescriptionCommand(Guid Id, string? Reason) : IRequest<Result>, IAuditableRequest
{
    public string AuditAction => "Prescription.Cancel";
    public string? AuditTarget => $"PrescriptionId={Id}";
}

public sealed class CancelPrescriptionCommandHandler : IRequestHandler<CancelPrescriptionCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Prescription> _prescriptionsRead;
    private readonly IRepository<Prescription> _prescriptionsWrite;
    private readonly TimeProvider _timeProvider;

    public CancelPrescriptionCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Prescription> prescriptionsRead,
        IRepository<Prescription> prescriptionsWrite,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _prescriptionsRead = prescriptionsRead;
        _prescriptionsWrite = prescriptionsWrite;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(CancelPrescriptionCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
            return Result.Failure("Auth.Unauthorized.UserContextMissing", "Kullanıcı bağlamı yok.");

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result.Failure("Tenants.NotFound", "Tenant bulunamadı.");
        if (!tenant.IsActive)
            return Result.Failure("Tenants.TenantInactive", "Pasif kiracı için reçete iptal edilemez.");

        var p = await _prescriptionsRead.FirstOrDefaultAsync(new PrescriptionByIdSpec(tenantId, request.Id), ct);
        if (p is null || (_clinicContext.ClinicId is { } clinicId && p.ClinicId != clinicId))
            return Result.Failure("Prescriptions.NotFound", "Reçete kaydı bulunamadı.");

        var access = await PrescriptionClinicWriteScope.EnsureWriteAccessAsync(_clinicScopeResolver, tenantId, p.ClinicId, ct);
        if (!access.IsSuccess)
            return access;

        var cancelled = p.Cancel(userId, request.Reason, _timeProvider.GetUtcNow().UtcDateTime);
        if (!cancelled.IsSuccess)
            return cancelled;

        await _prescriptionsWrite.UpdateAsync(p, ct);
        await _prescriptionsWrite.SaveChangesAsync(ct);
        return Result.Success();
    }
}
