using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Clinics.Commands.KvkkFormTemplate.UpdateClinicKvkkFormTemplate;

public sealed class UpdateClinicKvkkFormTemplateCommandHandler
    : IRequestHandler<UpdateClinicKvkkFormTemplateCommand, Result<ClinicKvkkFormTemplateDto>>
{
    private const string ConflictCode = "Clinics.KvkkFormTemplate.ConcurrencyConflict";
    private const string ConflictMessage = "Form metni başka biri tarafından değiştirildi; sayfayı yenileyip tekrar deneyin.";

    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IClinicAssignmentAccessGuard _assignmentGuard;
    private readonly IUserClinicRepository _userClinics;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IRepository<ClinicKvkkFormTemplate> _templates;
    private readonly TimeProvider _timeProvider;

    public UpdateClinicKvkkFormTemplateCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        ICurrentUserPermissionChecker permissions,
        IClinicAssignmentAccessGuard assignmentGuard,
        IUserClinicRepository userClinics,
        IReadRepository<Clinic> clinics,
        IRepository<ClinicKvkkFormTemplate> templates,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _permissions = permissions;
        _assignmentGuard = assignmentGuard;
        _userClinics = userClinics;
        _clinics = clinics;
        _templates = templates;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ClinicKvkkFormTemplateDto>> Handle(
        UpdateClinicKvkkFormTemplateCommand request,
        CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Clinics.Update))
        {
            return Result<ClinicKvkkFormTemplateDto>.Failure(
                "Auth.PermissionDenied",
                "KVKK şablon metnini güncellemek için Clinics.Update yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ClinicKvkkFormTemplateDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
        {
            return Result<ClinicKvkkFormTemplateDto>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, request.ClinicId), ct);
        if (clinic is null)
            return Result<ClinicKvkkFormTemplateDto>.Failure("Clinics.NotFound", "Klinik bulunamadı.");

        if (await _assignmentGuard.MustApplyAssignedClinicScopeAsync(userId, ct)
            && !await _userClinics.ExistsAsync(userId, clinic.Id, ct))
        {
            return Result<ClinicKvkkFormTemplateDto>.Failure(
                "Clinics.AccessDenied",
                "Bu klinik için atanmış üyeliğiniz yok.");
        }

        if (!ExaminationRowVersion.TryDecodeOptional(request.RowVersion, out var expectedRowVersion))
        {
            return Result<ClinicKvkkFormTemplateDto>.Failure(
                "Clinics.KvkkFormTemplate.Validation",
                "RowVersion geçerli Base64 (8 bayt) olmalıdır.");
        }

        var existing = await _templates.FirstOrDefaultAsync(
            new ClinicKvkkFormTemplateByClinicSpec(tenantId, request.ClinicId, request.Kind), ct);

        // Varsayılan metni görüp kaydeden istemci sürüm göndermez; arada kayıt oluştuysa (veya tersi) çakışmadır.
        if ((existing is null) != (expectedRowVersion is null))
            return Result<ClinicKvkkFormTemplateDto>.Failure(ConflictCode, ConflictMessage);

        ClinicKvkkFormTemplate row;
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        if (existing is null)
        {
            var created = ClinicKvkkFormTemplate.Create(tenantId, request.ClinicId, request.Kind, request.Text, userId, utcNow);
            if (!created.IsSuccess)
                return Result<ClinicKvkkFormTemplateDto>.Failure(created.Error);
            row = created.Value!;
        }
        else
        {
            var updated = existing.UpdateText(request.Text, userId, utcNow);
            if (!updated.IsSuccess)
                return Result<ClinicKvkkFormTemplateDto>.Failure(updated.Error);
            existing.SetExpectedRowVersion(expectedRowVersion!);
            row = existing;
        }

        try
        {
            if (existing is null)
                await _templates.AddAsync(row, ct);
            else
                await _templates.UpdateAsync(row, ct);
            await _templates.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ClinicKvkkFormTemplateDto>.Failure(ConflictCode, ConflictMessage);
        }
        catch (DbUpdateException) when (existing is null)
        {
            // Aynı klinik ve tür için eşzamanlı ilk kayıt: (TenantId, ClinicId, Kind) benzersiz indeksi kaybedene çarptı.
            return Result<ClinicKvkkFormTemplateDto>.Failure(ConflictCode, ConflictMessage);
        }

        return Result<ClinicKvkkFormTemplateDto>.Success(ClinicKvkkFormTemplateDto.From(row));
    }
}
