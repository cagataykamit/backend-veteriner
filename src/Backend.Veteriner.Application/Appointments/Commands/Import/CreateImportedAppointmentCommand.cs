using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Appointments.Commands.Import;

/// <summary>
/// İçe aktarma randevu yolu (ADR-048 T5, IMPORT_API_CONTRACT.md §12.3, §12.5): gelecek randevuyu partiye bağlı, planlı
/// durumda yazar. Slot, çakışma ve çalışma saati kuralları uygulanmaz (eski program zaten planlamıştı); hekime atama
/// bildirimi gönderilmez (toplu bildirim olmasın). Randevu olayı (outbox) mevcut oluşturma yoluyla aynı biçimde üretilir.
/// Hatırlatma, parti serbest bırakılana kadar hatırlatma işçisinde dışlanır. Geçmiş/mükerrer denetimi çağıran sahip
/// birimindedir; transaction sınırı birimdir.
/// </summary>
public sealed record CreateImportedAppointmentCommand(
    Guid ImportBatchId,
    Guid ClinicId,
    Guid PetId,
    DateTime ScheduledAtUtc,
    int DurationMinutes,
    AppointmentType AppointmentType,
    string? Notes,
    Guid? ReasonId,
    Guid? VeterinarianUserId)
    : IRequest<Result<Guid>>;

public sealed class CreateImportedAppointmentCommandHandler
    : IRequestHandler<CreateImportedAppointmentCommand, Result<Guid>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IRepository<Appointment> _appointments;
    private readonly IAppointmentProjectionSnapshotFactory _snapshotFactory;
    private readonly IAppointmentIntegrationEventOutbox _eventOutbox;
    private readonly TimeProvider _timeProvider;

    public CreateImportedAppointmentCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IRepository<Appointment> appointments,
        IAppointmentProjectionSnapshotFactory snapshotFactory,
        IAppointmentIntegrationEventOutbox eventOutbox,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinics = clinics;
        _pets = pets;
        _appointments = appointments;
        _snapshotFactory = snapshotFactory;
        _eventOutbox = eventOutbox;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<Guid>> Handle(CreateImportedAppointmentCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<Guid>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");

        if (!Appointment.IsValidDurationMinutes(request.DurationMinutes))
            return Result<Guid>.Failure("Appointments.Validation", "Randevu süresi 5-240 dakika arasında olmalıdır.");
        if (request.Notes is { Length: > Appointment.MaxNotesLength })
            return Result<Guid>.Failure("Appointments.Validation", $"Randevu notu en fazla {Appointment.MaxNotesLength} karakter olabilir.");

        if (await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, request.ClinicId), ct) is null)
            return Result<Guid>.Failure("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        if (await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, request.PetId), ct) is null)
            return Result<Guid>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var appointment = Appointment.CreateImported(
            tenantId,
            request.ClinicId,
            request.PetId,
            request.ScheduledAtUtc,
            request.DurationMinutes,
            request.AppointmentType,
            request.Notes,
            request.ReasonId,
            request.VeterinarianUserId,
            request.ImportBatchId);

        await _appointments.AddAsync(appointment, ct);

        var sequence = appointment.AdvanceMutationSequence();
        var current = await _snapshotFactory.CreateAsync(appointment, ct);
        await _eventOutbox.EnqueueAsync(
            AppointmentIntegrationEventTypes.Created,
            new AppointmentCreatedIntegrationEvent(Guid.NewGuid(), _timeProvider.GetUtcNow().UtcDateTime, sequence, current),
            ct);

        await _appointments.SaveChangesAsync(ct);
        return Result<Guid>.Success(appointment.Id);
    }
}
