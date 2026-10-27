using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hbs;
using Backend.Veteriner.Application.Hbs.Contracts;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Vaccinations.Access;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Hbs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Mediator;

namespace Backend.Veteriner.Application.Vaccinations.Commands.UpdateHbs;

/// <summary>
/// ATS aşı kaydı geri kaydı: HBS durumu, aşı uygulama belgesi seri no, "Gerekmiyor" gerekçesi (ADR-036 Dilim 2).
/// Denetim kaydı kim hangi numarayı girdiğini taşır.
/// </summary>
public sealed record UpdateVaccinationHbsCommand(Guid Id, HbsEntryStatus Status, string? ReferenceNumber, string? NotRequiredReason)
    : IRequest<Result<HbsEntryDto>>, IAuditableRequest
{
    public string AuditAction => "Vaccination.Hbs.Update";
    public string? AuditTarget => $"VaccinationId={Id}";
}

/// <summary>
/// Ayrı küçük yazma yolu: aşı PUT'u ve kesinleşmiş muayene kilidi (<c>ExaminationLinkedRecordLock</c>) çağrılmaz; HBS geri kaydı klinik içerik
/// değil ek bilgidir (ADR-013 kilidi klinik içeriği korur). Aşının klinik alanları ve bağlı uygulama değişmez.
/// </summary>
public sealed class UpdateVaccinationHbsCommandHandler : IRequestHandler<UpdateVaccinationHbsCommand, Result<HbsEntryDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Vaccination> _vaccinationsRead;
    private readonly IRepository<Vaccination> _vaccinationsWrite;
    private readonly IClinicVeterinarianReader _names;
    private readonly TimeProvider _timeProvider;

    public UpdateVaccinationHbsCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Vaccination> vaccinationsRead,
        IRepository<Vaccination> vaccinationsWrite,
        IClinicVeterinarianReader names,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _vaccinationsRead = vaccinationsRead;
        _vaccinationsWrite = vaccinationsWrite;
        _names = names;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<HbsEntryDto>> Handle(UpdateVaccinationHbsCommand request, CancellationToken ct)
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
            return Result<HbsEntryDto>.Failure("Tenants.TenantInactive", "Pasif kiracı için aşı kaydı güncellenemez.");

        var v = await _vaccinationsRead.FirstOrDefaultAsync(new VaccinationByIdSpec(tenantId, request.Id), ct);
        if (v is null || (_clinicContext.ClinicId is { } clinicId && v.ClinicId != clinicId))
            return Result<HbsEntryDto>.Failure("Vaccinations.NotFound", "Aşı kaydı bulunamadı.");

        var access = await VaccinationClinicWriteScope.EnsureWriteAccessAsync(_clinicScopeResolver, tenantId, v.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<HbsEntryDto>.Failure(access.Error);

        var applied = v.ApplyHbsEntry(request.Status, request.ReferenceNumber, request.NotRequiredReason, userId, _timeProvider.GetUtcNow().UtcDateTime);
        if (!applied.IsSuccess)
            return Result<HbsEntryDto>.Failure(applied.Error);

        await _vaccinationsWrite.UpdateAsync(v, ct);
        await _vaccinationsWrite.SaveChangesAsync(ct);

        return Result<HbsEntryDto>.Success(await HbsEntryDtoFactory.ForVaccinationAsync(v, _names, ct));
    }
}
