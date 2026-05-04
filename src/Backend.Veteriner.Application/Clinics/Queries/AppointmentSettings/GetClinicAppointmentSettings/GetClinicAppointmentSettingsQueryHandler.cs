using Backend.Veteriner.Application.Clinics.AppointmentSettings;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Queries.AppointmentSettings.GetClinicAppointmentSettings;

public sealed class GetClinicAppointmentSettingsQueryHandler
    : IRequestHandler<GetClinicAppointmentSettingsQuery, Result<ClinicAppointmentSettingsDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicAssignmentAccessGuard _assignmentGuard;
    private readonly IUserClinicRepository _userClinics;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<ClinicAppointmentSettings> _settingsRead;

    public GetClinicAppointmentSettingsQueryHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IClinicAssignmentAccessGuard assignmentGuard,
        IUserClinicRepository userClinics,
        IReadRepository<Clinic> clinics,
        IReadRepository<ClinicAppointmentSettings> settingsRead)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _assignmentGuard = assignmentGuard;
        _userClinics = userClinics;
        _clinics = clinics;
        _settingsRead = settingsRead;
    }

    public async Task<Result<ClinicAppointmentSettingsDto>> Handle(
        GetClinicAppointmentSettingsQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ClinicAppointmentSettingsDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
        {
            return Result<ClinicAppointmentSettingsDto>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, request.ClinicId), ct);
        if (clinic is null)
        {
            return Result<ClinicAppointmentSettingsDto>.Failure(
                "Clinics.NotFound",
                "Klinik bulunamadı.");
        }

        if (await _assignmentGuard.MustApplyAssignedClinicScopeAsync(userId, ct)
            && !await _userClinics.ExistsAsync(userId, clinic.Id, ct))
        {
            return Result<ClinicAppointmentSettingsDto>.Failure(
                "Clinics.AccessDenied",
                "Bu klinik için atanmış üyeliğiniz yok.");
        }

        var row = await _settingsRead.FirstOrDefaultAsync(
            new ClinicAppointmentSettingsByClinicSpec(tenantId, request.ClinicId), ct);

        if (row is null)
            return Result<ClinicAppointmentSettingsDto>.Success(ClinicAppointmentSettingsDefaults.Build());

        return Result<ClinicAppointmentSettingsDto>.Success(new ClinicAppointmentSettingsDto(
            row.DefaultAppointmentDurationMinutes,
            row.SlotIntervalMinutes,
            row.AllowOverlappingAppointments));
    }
}
