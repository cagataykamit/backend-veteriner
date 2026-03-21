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
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Appointment> _appointments;

    public GetAppointmentsListQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<Appointment> appointments)
    {
        _tenantContext = tenantContext;
        _appointments = appointments;
    }

    public async Task<Result<PagedResult<AppointmentListItemDto>>> Handle(
        GetAppointmentsListQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<AppointmentListItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);

        var total = await _appointments.CountAsync(
            new AppointmentsFilteredCountSpec(
                tenantId,
                request.ClinicId,
                request.PetId,
                request.Status,
                request.DateFromUtc,
                request.DateToUtc),
            ct);
        var rows = await _appointments.ListAsync(
            new AppointmentsFilteredPagedSpec(
                tenantId,
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
