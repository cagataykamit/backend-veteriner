using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Visits.Contracts;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.IntegrationEvents;
using Backend.Veteriner.Application.Visits.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Visits;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Visits.Commands.Create;

public sealed class CreateVisitCommandHandler : IRequestHandler<CreateVisitCommand, Result<VisitCreateResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IUserClinicRepository _userClinics;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Visit> _visitsRead;
    private readonly IRepository<Visit> _visitsWrite;
    private readonly IVisitIntegrationEventOutbox _eventOutbox;
    private readonly TimeProvider _timeProvider;

    public CreateVisitCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IUserClinicRepository userClinics,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IReadRepository<Appointment> appointments,
        IReadRepository<Visit> visitsRead,
        IRepository<Visit> visitsWrite,
        IVisitIntegrationEventOutbox eventOutbox,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _userClinics = userClinics;
        _tenants = tenants;
        _clinics = clinics;
        _pets = pets;
        _appointments = appointments;
        _visitsRead = visitsRead;
        _visitsWrite = visitsWrite;
        _eventOutbox = eventOutbox;
        _timeProvider = timeProvider;
    }

    public async Task<Result<VisitCreateResultDto>> Handle(CreateVisitCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<VisitCreateResultDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
        {
            return Result<VisitCreateResultDto>.Failure(
                "Auth.UserContextMissing",
                "Kullanıcı bağlamı bulunamadı.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<VisitCreateResultDto>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
        {
            return Result<VisitCreateResultDto>.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için geliş kaydı oluşturulamaz.");
        }

        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue
            && request.ClinicId.Value != _clinicContext.ClinicId.Value)
        {
            return Result<VisitCreateResultDto>.Failure(
                "Visits.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        Appointment? appointment = null;
        Guid clinicId;
        Guid petId;

        if (request.AppointmentId is { } appointmentId)
        {
            appointment = await _appointments.FirstOrDefaultAsync(
                new AppointmentByIdSpec(tenantId, appointmentId), ct);
            if (appointment is null)
            {
                return Result<VisitCreateResultDto>.Failure(
                    "Appointments.NotFound",
                    "Randevu bulunamadı veya kiracıya ait değil.");
            }

            clinicId = _clinicContext.ClinicId ?? request.ClinicId ?? appointment.ClinicId;
            petId = request.PetId ?? appointment.PetId;

            if (clinicId != appointment.ClinicId || petId != appointment.PetId)
            {
                return Result<VisitCreateResultDto>.Failure(
                    "Visits.AppointmentPetClinicMismatch",
                    "Seçilen randevu ile klinik veya hayvan bilgisi uyuşmuyor.");
            }
        }
        else
        {
            if ((_clinicContext.ClinicId ?? request.ClinicId) is not { } resolvedClinicId
                || request.PetId is not { } resolvedPetId)
            {
                return Result<VisitCreateResultDto>.Failure(
                    "Visits.Validation",
                    "AppointmentId yoksa ClinicId ve PetId zorunludur.");
            }

            clinicId = resolvedClinicId;
            petId = resolvedPetId;
        }

        var clinicAccess = await _clinicScopeResolver.ResolveAsync(tenantId, clinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<VisitCreateResultDto>.Failure(clinicAccess.Error);

        // Tekrar tıklama: randevu için mevcut geliş (tamamlanmış olsa bile) önce döner.
        if (appointment is not null)
        {
            var existingForAppointment = await _visitsRead.FirstOrDefaultAsync(
                new VisitByAppointmentIdSpec(tenantId, appointment.Id), ct);
            if (existingForAppointment is not null)
                return Existing(existingForAppointment);

            if (appointment.Status == AppointmentStatus.Cancelled)
            {
                return Result<VisitCreateResultDto>.Failure(
                    "Visits.AppointmentCancelled",
                    "İptal edilmiş randevu için geliş kaydı oluşturulamaz.");
            }

            if (appointment.Status != AppointmentStatus.Scheduled)
            {
                return Result<VisitCreateResultDto>.Failure(
                    "Visits.AppointmentNotScheduled",
                    "Yalnızca planlanmış randevu için geliş kaydı oluşturulabilir.");
            }
        }

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, clinicId), ct);
        if (clinic is null)
            return Result<VisitCreateResultDto>.Failure("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, petId), ct);
        if (pet is null)
            return Result<VisitCreateResultDto>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var existingActive = await _visitsRead.FirstOrDefaultAsync(
            new ActiveVisitByPetIdSpec(tenantId, petId), ct);
        if (existingActive is not null)
            return Existing(existingActive);

        if (request.ResponsibleVeterinarianUserId is { } vetId
            && !await _userClinics.ExistsActiveInTenantAsync(vetId, tenantId, clinicId, ct))
        {
            return Result<VisitCreateResultDto>.Failure(
                "Visits.Validation",
                "Sorumlu hekim bu kliniğe atanmış bir kullanıcı olmalıdır.");
        }

        var visit = new Visit(
            tenantId,
            clinicId,
            petId,
            appointment?.Id,
            request.ResponsibleVeterinarianUserId,
            userId,
            _timeProvider.GetUtcNow().UtcDateTime);

        await _visitsWrite.AddAsync(visit, ct);
        await _eventOutbox.EnqueueCreatedAsync(visit, ct);

        try
        {
            await _visitsWrite.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Eşzamanlı çift istek: filtreli benzersiz indeks kaybedene çarptı; kazanan geliş döner.
            var winner = (appointment is not null
                    ? await _visitsRead.FirstOrDefaultAsync(new VisitByAppointmentIdSpec(tenantId, appointment.Id), ct)
                    : null)
                ?? await _visitsRead.FirstOrDefaultAsync(new ActiveVisitByPetIdSpec(tenantId, petId), ct);
            if (winner is null)
                throw;

            return Existing(winner);
        }

        return Result<VisitCreateResultDto>.Success(new VisitCreateResultDto(true, visit.ToDto()));
    }

    private static Result<VisitCreateResultDto> Existing(Visit visit)
        => Result<VisitCreateResultDto>.Success(new VisitCreateResultDto(false, visit.ToDto()));
}
