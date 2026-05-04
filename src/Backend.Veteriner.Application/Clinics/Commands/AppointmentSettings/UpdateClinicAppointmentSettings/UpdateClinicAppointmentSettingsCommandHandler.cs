using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Commands.AppointmentSettings.UpdateClinicAppointmentSettings;

public sealed class UpdateClinicAppointmentSettingsCommandHandler
    : IRequestHandler<UpdateClinicAppointmentSettingsCommand, Result<ClinicAppointmentSettingsDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IClinicAssignmentAccessGuard _assignmentGuard;
    private readonly IUserClinicRepository _userClinics;
    private readonly IReadRepository<Clinic> _clinicsRead;
    private readonly IReadRepository<ClinicAppointmentSettings> _settingsRead;
    private readonly IRepository<ClinicAppointmentSettings> _settingsWrite;

    public UpdateClinicAppointmentSettingsCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        ICurrentUserPermissionChecker permissions,
        IClinicAssignmentAccessGuard assignmentGuard,
        IUserClinicRepository userClinics,
        IReadRepository<Clinic> clinicsRead,
        IReadRepository<ClinicAppointmentSettings> settingsRead,
        IRepository<ClinicAppointmentSettings> settingsWrite)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _permissions = permissions;
        _assignmentGuard = assignmentGuard;
        _userClinics = userClinics;
        _clinicsRead = clinicsRead;
        _settingsRead = settingsRead;
        _settingsWrite = settingsWrite;
    }

    public async Task<Result<ClinicAppointmentSettingsDto>> Handle(
        UpdateClinicAppointmentSettingsCommand request,
        CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Clinics.Update))
        {
            return Result<ClinicAppointmentSettingsDto>.Failure(
                "Auth.PermissionDenied",
                "Randevu varsayılanlarını güncellemek için Clinics.Update yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ClinicAppointmentSettingsDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
        {
            return Result<ClinicAppointmentSettingsDto>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        var clinic = await _clinicsRead.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, request.ClinicId), ct);
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

        var existing = await _settingsRead.FirstOrDefaultAsync(
            new ClinicAppointmentSettingsByClinicSpec(tenantId, request.ClinicId), ct);

        ClinicAppointmentSettings row;
        try
        {
            row = existing
                ?? ClinicAppointmentSettings.Create(
                    tenantId,
                    request.ClinicId,
                    request.DefaultAppointmentDurationMinutes,
                    request.SlotIntervalMinutes,
                    request.AllowOverlappingAppointments);

            if (existing is not null)
            {
                row.Update(
                    request.DefaultAppointmentDurationMinutes,
                    request.SlotIntervalMinutes,
                    request.AllowOverlappingAppointments);
            }
        }
        catch (ArgumentException ex)
        {
            return Result<ClinicAppointmentSettingsDto>.Failure(
                "Clinics.AppointmentSettings.Validation.InvalidRange",
                ex.Message);
        }

        if (existing is null)
            await _settingsWrite.AddAsync(row, ct);

        await _settingsWrite.SaveChangesAsync(ct);

        return Result<ClinicAppointmentSettingsDto>.Success(new ClinicAppointmentSettingsDto(
            row.DefaultAppointmentDurationMinutes,
            row.SlotIntervalMinutes,
            row.AllowOverlappingAppointments));
    }
}
