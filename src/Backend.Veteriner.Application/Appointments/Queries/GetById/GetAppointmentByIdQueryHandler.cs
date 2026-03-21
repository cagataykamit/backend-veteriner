using Backend.Veteriner.Application.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Appointments.Queries.GetById;

public sealed class GetAppointmentByIdQueryHandler
    : IRequestHandler<GetAppointmentByIdQuery, Result<AppointmentDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Appointment> _appointments;

    public GetAppointmentByIdQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<Appointment> appointments)
    {
        _tenantContext = tenantContext;
        _appointments = appointments;
    }

    public async Task<Result<AppointmentDetailDto>> Handle(GetAppointmentByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<AppointmentDetailDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var a = await _appointments.FirstOrDefaultAsync(
            new AppointmentByIdSpec(tenantId, request.Id), ct);
        if (a is null)
            return Result<AppointmentDetailDto>.Failure("Appointments.NotFound", "Randevu bulunamadı.");

        var dto = new AppointmentDetailDto(
            a.Id,
            a.TenantId,
            a.ClinicId,
            a.PetId,
            a.ScheduledAtUtc,
            a.Status,
            a.Notes);
        return Result<AppointmentDetailDto>.Success(dto);
    }
}
