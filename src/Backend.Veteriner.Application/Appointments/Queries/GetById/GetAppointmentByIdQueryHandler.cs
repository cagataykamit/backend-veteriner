using Backend.Veteriner.Application.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Appointments.Queries.GetById;

public sealed class GetAppointmentByIdQueryHandler
    : IRequestHandler<GetAppointmentByIdQuery, Result<AppointmentDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Clinic> _clinics;

    public GetAppointmentByIdQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Appointment> appointments,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        IReadRepository<Clinic> clinics)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _appointments = appointments;
        _pets = pets;
        _clients = clients;
        _clinics = clinics;
    }

    public async Task<Result<AppointmentDetailDto>> Handle(GetAppointmentByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<AppointmentDetailDto>.Failure(
                "Tenants.ContextMissing",
                "Kirac� ba�lam� yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var a = await _appointments.FirstOrDefaultAsync(
            new AppointmentByIdSpec(tenantId, request.Id), ct);
        if (a is null)
            return Result<AppointmentDetailDto>.Failure("Appointments.NotFound", "Randevu bulunamad�.");
        if (_clinicContext.ClinicId is { } clinicId && a.ClinicId != clinicId)
            return Result<AppointmentDetailDto>.Failure("Appointments.NotFound", "Randevu bulunamadi.");

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, a.PetId), ct);
        var clientName = string.Empty;
        if (pet is not null)
        {
            var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, pet.ClientId), ct);
            clientName = client?.FullName ?? string.Empty;
        }

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, a.ClinicId), ct);

        var dto = new AppointmentDetailDto(
            a.Id,
            a.TenantId,
            a.ClinicId,
            clinic?.Name ?? string.Empty,
            a.PetId,
            pet?.Name ?? string.Empty,
            clientName,
            a.ScheduledAtUtc,
            a.Status,
            a.Notes);
        return Result<AppointmentDetailDto>.Success(dto);
    }
}