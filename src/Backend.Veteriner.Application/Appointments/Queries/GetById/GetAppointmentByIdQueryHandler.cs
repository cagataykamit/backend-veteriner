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
    private readonly IReadRepository<Appointment> _appointments;

    public GetAppointmentByIdQueryHandler(IReadRepository<Appointment> appointments)
        => _appointments = appointments;

    public async Task<Result<AppointmentDetailDto>> Handle(GetAppointmentByIdQuery request, CancellationToken ct)
    {
        var a = await _appointments.FirstOrDefaultAsync(
            new AppointmentByIdSpec(request.TenantId, request.Id), ct);
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
