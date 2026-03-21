using Backend.Veteriner.Application.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Appointments.Queries.GetList;

public sealed class GetAppointmentsListQueryHandler
    : IRequestHandler<GetAppointmentsListQuery, Result<PagedResult<AppointmentListItemDto>>>
{
    private readonly IReadRepository<Appointment> _appointments;

    public GetAppointmentsListQueryHandler(IReadRepository<Appointment> appointments)
        => _appointments = appointments;

    public async Task<Result<PagedResult<AppointmentListItemDto>>> Handle(
        GetAppointmentsListQuery request,
        CancellationToken ct)
    {
        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);

        var total = await _appointments.CountAsync(
            new AppointmentsFilteredCountSpec(
                request.TenantId,
                request.ClinicId,
                request.PetId,
                request.Status,
                request.DateFromUtc,
                request.DateToUtc),
            ct);
        var rows = await _appointments.ListAsync(
            new AppointmentsFilteredPagedSpec(
                request.TenantId,
                request.ClinicId,
                request.PetId,
                request.Status,
                request.DateFromUtc,
                request.DateToUtc,
                page,
                pageSize),
            ct);

        var items = rows
            .Select(a => new AppointmentListItemDto(
                a.Id,
                a.TenantId,
                a.ClinicId,
                a.PetId,
                a.ScheduledAtUtc,
                a.Status))
            .ToList();

        return Result<PagedResult<AppointmentListItemDto>>.Success(
            PagedResult<AppointmentListItemDto>.Create(items, total, page, pageSize));
    }
}
