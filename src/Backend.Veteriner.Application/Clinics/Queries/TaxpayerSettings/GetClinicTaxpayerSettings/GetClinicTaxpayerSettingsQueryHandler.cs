using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Queries.TaxpayerSettings.GetClinicTaxpayerSettings;

/// <summary>Klinik mükellef ayarları; kayıt yoksa varsayılan (KDV %20, diğerleri boş) döner (ADR-036).</summary>
public sealed class GetClinicTaxpayerSettingsQueryHandler
    : IRequestHandler<GetClinicTaxpayerSettingsQuery, Result<ClinicTaxpayerSettingsDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicAssignmentAccessGuard _assignmentGuard;
    private readonly IUserClinicRepository _userClinics;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<ClinicTaxpayerSettings> _settings;

    public GetClinicTaxpayerSettingsQueryHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IClinicAssignmentAccessGuard assignmentGuard,
        IUserClinicRepository userClinics,
        IReadRepository<Clinic> clinics,
        IReadRepository<ClinicTaxpayerSettings> settings)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _assignmentGuard = assignmentGuard;
        _userClinics = userClinics;
        _clinics = clinics;
        _settings = settings;
    }

    public async Task<Result<ClinicTaxpayerSettingsDto>> Handle(
        GetClinicTaxpayerSettingsQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ClinicTaxpayerSettingsDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
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

        var row = await _settings.FirstOrDefaultAsync(new ClinicTaxpayerSettingsByClinicSpec(tenantId, clinic.Id), ct);
        return Result<ClinicTaxpayerSettingsDto>.Success(row is null
            ? ClinicTaxpayerSettingsDto.Default(clinic.Id)
            : ClinicTaxpayerSettingsDto.From(row));
    }
}
