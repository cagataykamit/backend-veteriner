using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Appointments.Commands.Complete;

public sealed class CompleteAppointmentCommandHandler : IRequestHandler<CompleteAppointmentCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Appointment> _appointmentsRead;
    private readonly IRepository<Appointment> _appointmentsWrite;

    public CompleteAppointmentCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Appointment> appointmentsRead,
        IRepository<Appointment> appointmentsWrite)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _appointmentsRead = appointmentsRead;
        _appointmentsWrite = appointmentsWrite;
    }

    public async Task<Result> Handle(CompleteAppointmentCommand request, CancellationToken ct)
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
        if (_clinicContext.ClinicId is { } clinicId && appointment.ClinicId != clinicId)
            return Result.Failure("Appointments.NotFound", "Randevu bulunamadi veya kiraciya ait degil.");

        var domain = appointment.Complete();
        if (!domain.IsSuccess)
            return Result.Failure(domain.Error);

        await _appointmentsWrite.SaveChangesAsync(ct);
        return Result.Success();
    }
}
