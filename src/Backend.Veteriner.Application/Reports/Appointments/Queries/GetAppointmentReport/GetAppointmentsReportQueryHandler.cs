using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Appointments;
using Backend.Veteriner.Application.Reports.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.Appointments.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Appointments.Queries.GetAppointmentReport;

public sealed class GetAppointmentsReportQueryHandler
    : IRequestHandler<GetAppointmentsReportQuery, Result<AppointmentReportResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Clinic> _clinics;

    public GetAppointmentsReportQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Appointment> appointments,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _appointments = appointments;
        _clients = clients;
        _pets = pets;
        _clinics = clinics;
    }

    public async Task<Result<AppointmentReportResultDto>> Handle(
        GetAppointmentsReportQuery request,
        CancellationToken ct)
    {
        var validated = await AppointmentsReportQueryValidation.ValidateAsync(
            _tenantContext,
            _clinicContext,
            _clinics,
            request.ClinicId,
            request.FromUtc,
            request.ToUtc,
            ct);
        if (!validated.IsSuccess)
            return Result<AppointmentReportResultDto>.Failure(validated.Error);

        var (tenantId, effectiveClinicId, fromUtc, toUtc) = validated.Value!;

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

        var total = await _appointments.CountAsync(
            new AppointmentsReportFilteredCountSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                restrictedPetIds,
                request.Status,
                fromUtc,
                toUtc,
                searchPattern.Pattern,
                searchPattern.PetIds),
            ct);

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
                searchPattern.PetIds),
            ct);

        var items = await AppointmentsReportItemMapping.MapAsync(tenantId, rows, _clients, _pets, _clinics, ct);

        var scheduled = await _appointments.CountAsync(
            new AppointmentsReportFilteredCountSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                restrictedPetIds,
                AppointmentStatus.Scheduled,
                fromUtc,
                toUtc,
                searchPattern.Pattern,
                searchPattern.PetIds),
            ct);
        var completed = await _appointments.CountAsync(
            new AppointmentsReportFilteredCountSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                restrictedPetIds,
                AppointmentStatus.Completed,
                fromUtc,
                toUtc,
                searchPattern.Pattern,
                searchPattern.PetIds),
            ct);
        var cancelled = await _appointments.CountAsync(
            new AppointmentsReportFilteredCountSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                restrictedPetIds,
                AppointmentStatus.Cancelled,
                fromUtc,
                toUtc,
                searchPattern.Pattern,
                searchPattern.PetIds),
            ct);

        var statusCounts = new AppointmentReportStatusCountsDto(scheduled, completed, cancelled);

        return Result<AppointmentReportResultDto>.Success(
            new AppointmentReportResultDto(total, items, statusCounts));
    }
}
