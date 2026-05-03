using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clinics.WorkingHours;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Queries.WorkingHours.GetClinicWorkingHours;

public sealed class GetClinicWorkingHoursQueryHandler
    : IRequestHandler<GetClinicWorkingHoursQuery, Result<IReadOnlyList<ClinicWorkingHourDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicAssignmentAccessGuard _assignmentGuard;
    private readonly IUserClinicRepository _userClinics;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<ClinicWorkingHour> _hoursRead;

    public GetClinicWorkingHoursQueryHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IClinicAssignmentAccessGuard assignmentGuard,
        IUserClinicRepository userClinics,
        IReadRepository<Clinic> clinics,
        IReadRepository<ClinicWorkingHour> hoursRead)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _assignmentGuard = assignmentGuard;
        _userClinics = userClinics;
        _clinics = clinics;
        _hoursRead = hoursRead;
    }

    public async Task<Result<IReadOnlyList<ClinicWorkingHourDto>>> Handle(
        GetClinicWorkingHoursQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<IReadOnlyList<ClinicWorkingHourDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
        {
            return Result<IReadOnlyList<ClinicWorkingHourDto>>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, request.ClinicId), ct);
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

        var rows = await _hoursRead.ListAsync(new ClinicWorkingHoursByClinicSpec(tenantId, request.ClinicId), ct);
        if (rows.Count == 0)
            return Result<IReadOnlyList<ClinicWorkingHourDto>>.Success(ClinicWorkingHoursDefaults.BuildWeek());

        var dtos = rows
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
