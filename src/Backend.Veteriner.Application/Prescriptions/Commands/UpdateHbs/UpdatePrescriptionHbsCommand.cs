using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hbs;
using Backend.Veteriner.Application.Hbs.Contracts;
using Backend.Veteriner.Application.Prescriptions.Access;
using Backend.Veteriner.Application.Prescriptions.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Hbs;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;

namespace Backend.Veteriner.Application.Prescriptions.Commands.UpdateHbs;

/// <summary>
/// e-Reçete geri kaydı: HBS durumu, e-Reçete No, "Gerekmiyor" gerekçesi (ADR-036 Dilim 2). Denetim kaydı kim hangi numarayı girdiğini taşır.
/// </summary>
public sealed record UpdatePrescriptionHbsCommand(Guid Id, HbsEntryStatus Status, string? ReferenceNumber, string? NotRequiredReason)
    : IRequest<Result<HbsEntryDto>>, IAuditableRequest
{
    public string AuditAction => "Prescription.Hbs.Update";
    public string? AuditTarget => $"PrescriptionId={Id}";
}

/// <summary>
/// Ayrı küçük yazma yolu: reçete PUT'u ve kesinleşmiş muayene kilidi (<c>ExaminationLinkedRecordLock</c>) çağrılmaz; HBS geri kaydı klinik içerik
/// değil ek bilgidir (ADR-013 kilidi klinik içeriği korur). Klinik içerik ve kalemler değişmez.
/// </summary>
public sealed class UpdatePrescriptionHbsCommandHandler : IRequestHandler<UpdatePrescriptionHbsCommand, Result<HbsEntryDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Prescription> _prescriptionsRead;
    private readonly IRepository<Prescription> _prescriptionsWrite;
    private readonly IClinicVeterinarianReader _names;

    public UpdatePrescriptionHbsCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Prescription> prescriptionsRead,
        IRepository<Prescription> prescriptionsWrite,
        IClinicVeterinarianReader names)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _prescriptionsRead = prescriptionsRead;
        _prescriptionsWrite = prescriptionsWrite;
        _names = names;
    }

    public async ValueTask<Result<HbsEntryDto>> Handle(UpdatePrescriptionHbsCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<HbsEntryDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
            return Result<HbsEntryDto>.Failure("Auth.Unauthorized.UserContextMissing", "Kullanıcı bağlamı yok.");

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<HbsEntryDto>.Failure("Tenants.NotFound", "Tenant bulunamadı.");
        if (!tenant.IsActive)
            return Result<HbsEntryDto>.Failure("Tenants.TenantInactive", "Pasif kiracı için reçete kaydı güncellenemez.");

        var p = await _prescriptionsRead.FirstOrDefaultAsync(new PrescriptionByIdSpec(tenantId, request.Id), ct);
        if (p is null || (_clinicContext.ClinicId is { } clinicId && p.ClinicId != clinicId))
            return Result<HbsEntryDto>.Failure("Prescriptions.NotFound", "Reçete kaydı bulunamadı.");

        var access = await PrescriptionClinicWriteScope.EnsureWriteAccessAsync(_clinicScopeResolver, tenantId, p.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<HbsEntryDto>.Failure(access.Error);

        var applied = p.ApplyHbsEntry(request.Status, request.ReferenceNumber, request.NotRequiredReason, userId, DateTime.UtcNow);
        if (!applied.IsSuccess)
            return Result<HbsEntryDto>.Failure(applied.Error);

        await _prescriptionsWrite.UpdateAsync(p, ct);
        await _prescriptionsWrite.SaveChangesAsync(ct);

        return Result<HbsEntryDto>.Success(await HbsEntryDtoFactory.ForPrescriptionAsync(p, _names, ct));
    }
}
