using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Commands.WorkingHours.UpdateClinicWorkingHours;

public sealed class UpdateClinicWorkingHoursCommandHandler
    : IRequestHandler<UpdateClinicWorkingHoursCommand, Result<IReadOnlyList<ClinicWorkingHourDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IClinicAssignmentAccessGuard _assignmentGuard;
    private readonly IUserClinicRepository _userClinics;
    private readonly IReadRepository<Clinic> _clinicsRead;
    private readonly IReadRepository<ClinicWorkingHour> _hoursRead;
    private readonly IRepository<ClinicWorkingHour> _hoursWrite;

    public UpdateClinicWorkingHoursCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        ICurrentUserPermissionChecker permissions,
        IClinicAssignmentAccessGuard assignmentGuard,
        IUserClinicRepository userClinics,
        IReadRepository<Clinic> clinicsRead,
        IReadRepository<ClinicWorkingHour> hoursRead,
        IRepository<ClinicWorkingHour> hoursWrite)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _permissions = permissions;
        _assignmentGuard = assignmentGuard;
        _userClinics = userClinics;
        _clinicsRead = clinicsRead;
        _hoursRead = hoursRead;
        _hoursWrite = hoursWrite;
    }

    public async Task<Result<IReadOnlyList<ClinicWorkingHourDto>>> Handle(
        UpdateClinicWorkingHoursCommand request,
        CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Clinics.Update))
        {
            return Result<IReadOnlyList<ClinicWorkingHourDto>>.Failure(
                "Auth.PermissionDenied",
                "Çalışma saatlerini güncellemek için Clinics.Update yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<IReadOnlyList<ClinicWorkingHourDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
        {
            return Result<IReadOnlyList<ClinicWorkingHourDto>>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        var clinic = await _clinicsRead.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, request.ClinicId), ct);
        if (clinic is null)
        {
            return Result<IReadOnlyList<ClinicWorkingHourDto>>.Failure(
                "Clinics.NotFound",
                "Klinik bulunamadı.");
        }

        if (await _assignmentGuard.MustApplyAssignedClinicScopeAsync(userId, ct))
        {
            if (!await _userClinics.ExistsAsync(userId, clinic.Id, ct))
            {
                return Result<IReadOnlyList<ClinicWorkingHourDto>>.Failure(
                    "Clinics.AccessDenied",
                    "Bu klinik için atanmış üyeliğiniz yok.");
            }
        }

        var existing = await _hoursRead.ListAsync(new ClinicWorkingHoursByClinicSpec(tenantId, request.ClinicId), ct);
        foreach (var row in existing)
            await _hoursWrite.DeleteAsync(row, ct);

        foreach (var item in request.Items.OrderBy(i => i.DayOfWeek))
        {
            try
            {
                var entity = ClinicWorkingHour.Create(
                    tenantId,
                    request.ClinicId,
                    item.DayOfWeek,
                    item.IsClosed,
                    item.OpensAt,
                    item.ClosesAt,
                    item.BreakStartsAt,
                    item.BreakEndsAt);
                await _hoursWrite.AddAsync(entity, ct);
            }
            catch (ArgumentException ex)
            {
                return Result<IReadOnlyList<ClinicWorkingHourDto>>.Failure(
                    "Clinics.WorkingHours.Validation.InvalidSchedule",
                    ex.Message);
            }
        }

        await _hoursWrite.SaveChangesAsync(ct);

        var saved = await _hoursRead.ListAsync(new ClinicWorkingHoursByClinicSpec(tenantId, request.ClinicId), ct);
        var dtos = saved
            .Select(r => new ClinicWorkingHourDto(
                r.DayOfWeek,
                r.IsClosed,
                r.OpensAt,
                r.ClosesAt,
                r.BreakStartsAt,
                r.BreakEndsAt))
            .ToList();

        return Result<IReadOnlyList<ClinicWorkingHourDto>>.Success(dtos);
    }
}
