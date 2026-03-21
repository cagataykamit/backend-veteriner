using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Appointments.Commands.Cancel;

public sealed class CancelAppointmentCommandHandler : IRequestHandler<CancelAppointmentCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Appointment> _appointmentsRead;
    private readonly IRepository<Appointment> _appointmentsWrite;

    public CancelAppointmentCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<Appointment> appointmentsRead,
        IRepository<Appointment> appointmentsWrite)
    {
        _tenantContext = tenantContext;
        _appointmentsRead = appointmentsRead;
        _appointmentsWrite = appointmentsWrite;
    }

    public async Task<Result> Handle(CancelAppointmentCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var appointment = await _appointmentsRead.FirstOrDefaultAsync(
            new AppointmentByIdSpec(tenantId, request.AppointmentId), ct);

        if (appointment is null)
            return Result.Failure("Appointments.NotFound", "Randevu bulunamadı veya kiracıya ait değil.");

        var domain = appointment.Cancel(request.Reason);
        if (!domain.IsSuccess)
            return Result.Failure(domain.Error);

        await _appointmentsWrite.SaveChangesAsync(ct);
        return Result.Success();
    }
}
