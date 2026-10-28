using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Appointments;
using Backend.Veteriner.Application.Reports.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.Appointments.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Reports.Appointments.Queries.GetAppointmentReport;

public sealed class GetAppointmentsReportQueryHandler
    : IRequestHandler<GetAppointmentsReportQuery, Result<AppointmentReportResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IAppointmentsReportStatusBreakdownReader _statusBreakdown;

    public GetAppointmentsReportQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Appointment> appointments,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics,
        IAppointmentsReportStatusBreakdownReader statusBreakdown)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _appointments = appointments;
        _clients = clients;
        _pets = pets;
        _clinics = clinics;
        _statusBreakdown = statusBreakdown;
    }

    public async ValueTask<Result<AppointmentReportResultDto>> Handle(
        GetAppointmentsReportQuery request,
        CancellationToken ct)
    {
        var validated = await AppointmentsReportQueryValidation.ValidateAsync(
            _tenantContext,
            _clinicContext,
            _clinicScopeResolver,
            request.ClinicId,
            request.FromUtc,
            request.ToUtc,
            ct);
        if (!validated.IsSuccess)
            return Result<AppointmentReportResultDto>.Failure(validated.Error);

        var (tenantId, effectiveClinicId, accessibleClinicIds, fromUtc, toUtc) = validated.Value!;

        var restricted = await AppointmentsReportClientPetFilter.ResolveAsync(
            tenantId,
            request.ClientId,
            request.PetId,
            _pets,
            ct);
        if (!restricted.IsSuccess)
            return Result<AppointmentReportResultDto>.Failure(restricted.Error);
        if (restricted.Value!.SkipQueryEmpty)
        {
            var emptyCounts = new AppointmentReportStatusCountsDto(0, 0, 0);
            return Result<AppointmentReportResultDto>.Success(
                new AppointmentReportResultDto(0, [], emptyCounts));
        }

        var restrictedPetIds = restricted.Value.RestrictedPetIds;

        var searchPattern = await AppointmentsReportSearchHelper.ResolveAsync(
            tenantId,
            request.Search,
            _clients,
            _pets,
            ct);

        var statusRows = await _statusBreakdown.GetAsync(
            tenantId,
            effectiveClinicId,
            request.PetId,
            restrictedPetIds,
            fromUtc,
            toUtc,
            searchPattern.Pattern,
            searchPattern.PetIds,
            accessibleClinicIds,
            ct);

        var statusCounts = AppointmentStatusCounts.From(statusRows);
        var breakdownSum = statusCounts.Scheduled + statusCounts.Completed + statusCounts.Cancelled + statusCounts.NoShow;
        var total = request.Status.HasValue
            ? request.Status.Value switch
            {
                AppointmentStatus.Scheduled => statusCounts.Scheduled,
                AppointmentStatus.Completed => statusCounts.Completed,
                AppointmentStatus.Cancelled => statusCounts.Cancelled,
                AppointmentStatus.NoShow => statusCounts.NoShow,
                _ => breakdownSum,
            }
            : breakdownSum;

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, AppointmentsReportConstants.MaxPageSize);

        var rows = await _appointments.ListAsync(
            new AppointmentsReportFilteredPagedSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                restrictedPetIds,
                request.Status,
                fromUtc,
                toUtc,
                page,
                pageSize,
                searchPattern.Pattern,
                searchPattern.PetIds,
                accessibleClinicIds),
            ct);

        var items = await AppointmentsReportItemMapping.MapAsync(tenantId, rows, _clients, _pets, _clinics, ct);

        return Result<AppointmentReportResultDto>.Success(
            new AppointmentReportResultDto(total, items, statusCounts));
    }
}
