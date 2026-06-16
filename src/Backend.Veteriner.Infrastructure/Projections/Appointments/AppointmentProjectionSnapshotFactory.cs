using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Projections.Appointments;

public sealed class AppointmentProjectionSnapshotFactory : IAppointmentProjectionSnapshotFactory
{
    private readonly AppDbContext _db;

    public AppointmentProjectionSnapshotFactory(AppDbContext db) => _db = db;

    public async Task<AppointmentProjectionSnapshot> CreateAsync(
        Appointment appointment,
        CancellationToken cancellationToken = default)
    {
        var display = await (
                from p in _db.Pets.AsNoTracking()
                join c in _db.Clinics.AsNoTracking()
                    on new { p.TenantId, ClinicId = appointment.ClinicId }
                    equals new { c.TenantId, ClinicId = c.Id }
                join s in _db.Species.AsNoTracking() on p.SpeciesId equals s.Id
                join cl in _db.Clients.AsNoTracking()
                    on new { p.TenantId, p.ClientId }
                    equals new { cl.TenantId, ClientId = cl.Id }
                join br in _db.Breeds.AsNoTracking() on p.BreedId equals br.Id into breedJoin
                from br in breedJoin.DefaultIfEmpty()
                where p.TenantId == appointment.TenantId && p.Id == appointment.PetId
                select new DisplayRow(
                    c.Name,
                    p.Name,
                    p.SpeciesId,
                    s.Name,
                    p.Breed,
                    br != null ? br.Name : null,
                    p.ClientId,
                    cl.FullName,
                    cl.Phone,
                    cl.Email,
                    cl.PhoneNormalized))
            .FirstOrDefaultAsync(cancellationToken);

        if (display is null)
        {
            throw new InvalidOperationException(
                $"Appointment projection snapshot için clinic/pet/client bilgisi bulunamadı. " +
                $"TenantId={appointment.TenantId}, ClinicId={appointment.ClinicId}, PetId={appointment.PetId}");
        }

        return MapScalars(appointment, display);
    }

    public AppointmentProjectionSnapshot CreateScalarsFromPrevious(
        Appointment appointment,
        AppointmentProjectionSnapshot previous)
        => previous with
        {
            AppointmentId = appointment.Id,
            TenantId = appointment.TenantId,
            ClinicId = appointment.ClinicId,
            PetId = appointment.PetId,
            ScheduledAtUtc = appointment.ScheduledAtUtc,
            DurationMinutes = appointment.DurationMinutes,
            AppointmentType = (int)appointment.AppointmentType,
            Status = (int)appointment.Status,
            Notes = appointment.Notes
        };

    private static AppointmentProjectionSnapshot MapScalars(Appointment appointment, DisplayRow display)
        => new(
            appointment.Id,
            appointment.TenantId,
            appointment.ClinicId,
            display.ClinicName,
            appointment.PetId,
            display.PetName,
            display.SpeciesId,
            display.SpeciesName,
            display.ClientId,
            display.ClientName,
            display.ClientPhone,
            appointment.ScheduledAtUtc,
            appointment.DurationMinutes,
            (int)appointment.AppointmentType,
            (int)appointment.Status,
            appointment.Notes,
            display.PetBreed,
            display.PetBreedRefName,
            display.ClientEmail,
            display.ClientPhoneNormalized);

    private sealed record DisplayRow(
        string ClinicName,
        string PetName,
        Guid SpeciesId,
        string SpeciesName,
        string? PetBreed,
        string? PetBreedRefName,
        Guid ClientId,
        string ClientName,
        string? ClientPhone,
        string? ClientEmail,
        string? ClientPhoneNormalized);
}
