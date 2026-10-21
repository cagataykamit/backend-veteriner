using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Clinics.Commands.TaxpayerSettings.UpdateClinicTaxpayerSettings;

public sealed class UpdateClinicTaxpayerSettingsCommandHandler
    : IRequestHandler<UpdateClinicTaxpayerSettingsCommand, Result<ClinicTaxpayerSettingsDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IClinicAssignmentAccessGuard _assignmentGuard;
    private readonly IUserClinicRepository _userClinics;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IRepository<ClinicTaxpayerSettings> _settings;

    public UpdateClinicTaxpayerSettingsCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        ICurrentUserPermissionChecker permissions,
        IClinicAssignmentAccessGuard assignmentGuard,
        IUserClinicRepository userClinics,
        IReadRepository<Clinic> clinics,
        IRepository<ClinicTaxpayerSettings> settings)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _permissions = permissions;
        _assignmentGuard = assignmentGuard;
        _userClinics = userClinics;
        _clinics = clinics;
        _settings = settings;
    }

    public async Task<Result<ClinicTaxpayerSettingsDto>> Handle(
        UpdateClinicTaxpayerSettingsCommand request,
        CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Clinics.Update))
        {
            return Result<ClinicTaxpayerSettingsDto>.Failure(
                "Auth.PermissionDenied",
                "Mükellef bilgilerini güncellemek için Clinics.Update yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ClinicTaxpayerSettingsDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
        {
            return Result<ClinicTaxpayerSettingsDto>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, request.ClinicId), ct);
        if (clinic is null)
            return Result<ClinicTaxpayerSettingsDto>.Failure("Clinics.NotFound", "Klinik bulunamadı.");

        if (await _assignmentGuard.MustApplyAssignedClinicScopeAsync(userId, ct)
            && !await _userClinics.ExistsAsync(userId, clinic.Id, ct))
        {
            return Result<ClinicTaxpayerSettingsDto>.Failure(
                "Clinics.AccessDenied",
                "Bu klinik için atanmış üyeliğiniz yok.");
        }

        var vatRate = request.DefaultVatRate ?? TaxRatePercent.DefaultVatRate;
        var existing = await _settings.FirstOrDefaultAsync(new ClinicTaxpayerSettingsByClinicSpec(tenantId, clinic.Id), ct);
        ClinicTaxpayerSettings row;
        if (existing is null)
        {
            var created = ClinicTaxpayerSettings.Create(
                tenantId,
                clinic.Id,
                request.TaxpayerType,
                request.TaxIdentityNumber,
                request.TaxOffice,
                request.TaxpayerName,
                request.Province,
                request.District,
                request.AddressLine,
                vatRate,
                request.WithholdingRate,
                userId);
            if (!created.IsSuccess)
                return Result<ClinicTaxpayerSettingsDto>.Failure(created.Error);
            row = created.Value!;
        }
        else
        {
            var updated = existing.Update(
                request.TaxpayerType,
                request.TaxIdentityNumber,
                request.TaxOffice,
                request.TaxpayerName,
                request.Province,
                request.District,
                request.AddressLine,
                vatRate,
                request.WithholdingRate,
                userId);
            if (!updated.IsSuccess)
                return Result<ClinicTaxpayerSettingsDto>.Failure(updated.Error);
            row = existing;
        }

        try
        {
            if (existing is null)
                await _settings.AddAsync(row, ct);
            else
                await _settings.UpdateAsync(row, ct);
            await _settings.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (existing is null)
        {
            // Aynı klinik için eşzamanlı ilk kayıt: (TenantId, ClinicId) benzersiz indeksi kaybedene çarptı.
            return Result<ClinicTaxpayerSettingsDto>.Failure(
                "Clinics.TaxpayerSettings.ConcurrencyConflict",
                "Mükellef bilgileri aynı anda kaydedildi; sayfayı yenileyip tekrar deneyin.");
        }

        return Result<ClinicTaxpayerSettingsDto>.Success(ClinicTaxpayerSettingsDto.From(row));
    }
}
