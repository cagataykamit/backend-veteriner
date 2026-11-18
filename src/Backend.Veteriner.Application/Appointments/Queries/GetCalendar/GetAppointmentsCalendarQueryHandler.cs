using Backend.Veteriner.Application.Appointments.Calendar;
using Backend.Veteriner.Application.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Appointments.ReadModels;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Appointments.Queries.GetCalendar;

public sealed class GetAppointmentsCalendarQueryHandler
    : IRequestHandler<GetAppointmentsCalendarQuery, Result<IReadOnlyList<AppointmentCalendarItemDto>>>
{
    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(45);

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly IAppointmentReadModelReader _readModelReader;
    private readonly QueryReadModelsOptions _queryReadModelsOptions;
    private readonly IClinicVeterinarianReader _veterinarians;
    private readonly IReadRepository<AppointmentReason> _reasons;
    private readonly TimeProvider _timeProvider;

    public GetAppointmentsCalendarQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Appointment> appointments,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        IAppointmentReadModelReader readModelReader,
        IOptions<QueryReadModelsOptions> queryReadModelsOptions,
        IClinicVeterinarianReader veterinarians,
        IReadRepository<AppointmentReason> reasons,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _appointments = appointments;
        _pets = pets;
        _clients = clients;
        _readModelReader = readModelReader;
        _queryReadModelsOptions = queryReadModelsOptions.Value;
        _veterinarians = veterinarians;
        _reasons = reasons;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<IReadOnlyList<AppointmentCalendarItemDto>>> Handle(GetAppointmentsCalendarQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<IReadOnlyList<AppointmentCalendarItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (!request.DateFromUtc.HasValue || !request.DateToUtc.HasValue)
        {
            return Result<IReadOnlyList<AppointmentCalendarItemDto>>.Failure(
                "Appointments.Calendar.DateWindowRequired",
                "dateFromUtc ve dateToUtc zorunludur.");
        }

        var dateFromUtc = NormalizeToUtc(request.DateFromUtc.Value);
        var dateToUtc = NormalizeToUtc(request.DateToUtc.Value);
        if (dateToUtc <= dateFromUtc)
        {
            return Result<IReadOnlyList<AppointmentCalendarItemDto>>.Failure(
                "Appointments.Calendar.InvalidDateWindow",
                "dateToUtc, dateFromUtc'den büyük olmalıdır.");
        }

        if (dateToUtc - dateFromUtc > MaxWindow)
        {
            return Result<IReadOnlyList<AppointmentCalendarItemDto>>.Failure(
                "Appointments.Calendar.DateWindowTooLarge",
                "Tarih aralığı en fazla 45 gün olabilir.");
        }

        if (ClinicContextRule.Conflicts(request.ClinicId, _clinicContext.ClinicId))
        {
            return Result<IReadOnlyList<AppointmentCalendarItemDto>>.Failure(ClinicContextRule.Mismatch("Appointments"));
        }

        var requestedClinicId = request.ClinicId ?? _clinicContext.ClinicId;
        if (requestedClinicId is null)
        {
            return Result<IReadOnlyList<AppointmentCalendarItemDto>>.Failure(
                "Appointments.ClinicScopeRequired",
                "Klinik kapsamı gerekli: aktif klinik bağlamı yok ve clinicId belirtilmedi. Takvim klinik kapsamı olmadan listelenemez.");
        }

        var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, requestedClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<IReadOnlyList<AppointmentCalendarItemDto>>.Failure(scopeResult.Error);

        var effectiveClinicId = scopeResult.Value!.SingleClinicId ?? requestedClinicId.Value;
        var filter = new AppointmentCalendarFilter(request.VeterinarianUserId, request.WithoutVeterinarian, request.AppointmentTypes, request.States);
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        if (_queryReadModelsOptions.AppointmentsEnabled)
        {
            var items = await _readModelReader.GetCalendarAsync(
                new AppointmentCalendarReadRequest(
                    new AppointmentReadScope(tenantId, effectiveClinicId),
                    dateFromUtc,
                    dateToUtc,
                    request.Status),
                ct);

            var detailed = await WithDetailsFromCommandDbAsync(tenantId, items, ct);
            return Result<IReadOnlyList<AppointmentCalendarItemDto>>.Success(WithState(filter.IsEmpty
                ? detailed
                : detailed.Where(i => filter.Matches(i.VeterinarianUserId, i.AppointmentType, i.Status, i.ScheduledAtUtc, utcNow)).ToList(), utcNow));
        }

        return await HandleFromCommandDbAsync(
            tenantId,
            effectiveClinicId,
            dateFromUtc,
            dateToUtc,
            request.Status,
            filter,
            utcNow,
            ct);
    }

    private async Task<Result<IReadOnlyList<AppointmentCalendarItemDto>>> HandleFromCommandDbAsync(
        Guid tenantId,
        Guid effectiveClinicId,
        DateTime dateFromUtc,
        DateTime dateToUtc,
        AppointmentStatus? status,
        AppointmentCalendarFilter filter,
        DateTime utcNow,
        CancellationToken ct)
    {
        var all = await _appointments.ListAsync(
            new AppointmentsCalendarSpec(
                tenantId,
                effectiveClinicId,
                dateFromUtc,
                dateToUtc,
                status),
            ct);
        var rows = filter.IsEmpty
            ? all
            : all.Where(r => filter.Matches(r.VeterinarianUserId, r.AppointmentType, r.Status, r.ScheduledAtUtc, utcNow)).ToList();

        if (rows.Count == 0)
            return Result<IReadOnlyList<AppointmentCalendarItemDto>>.Success(Array.Empty<AppointmentCalendarItemDto>());

        var petIds = rows.Select(r => r.PetId).Distinct().ToArray();
        var pets = await _pets.ListAsync(new PetsByTenantIdsNameClientSpec(tenantId, petIds), ct);
        var petById = pets.ToDictionary(p => p.Id);

        var clientIds = pets.Select(p => p.ClientId).Distinct().ToArray();
        var clients = clientIds.Length == 0
            ? []
            : await _clients.ListAsync(new ClientsByTenantIdsNameSpec(tenantId, clientIds), ct);
        var clientNameById = clients.ToDictionary(c => c.Id, c => c.FullName);

        var items = rows.Select(r =>
        {
            petById.TryGetValue(r.PetId, out var pet);
            var clientId = pet?.ClientId ?? Guid.Empty;
            var clientName = clientId != Guid.Empty && clientNameById.TryGetValue(clientId, out var cName)
                ? cName
                : string.Empty;
            var scheduledEndUtc = r.ScheduledAtUtc.AddMinutes(r.DurationMinutes);
            return new AppointmentCalendarItemDto(
                r.Id,
                r.ClinicId,
                r.PetId,
                clientId,
                r.ScheduledAtUtc,
                r.DurationMinutes,
                scheduledEndUtc,
                r.Status,
                r.AppointmentType,
                pet?.Name ?? string.Empty,
                clientName,
                r.VeterinarianUserId,
                ReasonId: r.ReasonId,
                Notes: r.Notes);
        }).ToList();

        return Result<IReadOnlyList<AppointmentCalendarItemDto>>.Success(WithState(await WithNamesAsync(tenantId, items, ct), utcNow));
    }

    /// <summary>
    /// Okuma modeli hekim, neden ve not taşımaz: bunlar komut veritabanından tamamlanır (Query DB şeması değişmez).
    /// </summary>
    private async Task<IReadOnlyList<AppointmentCalendarItemDto>> WithDetailsFromCommandDbAsync(
        Guid tenantId,
        IReadOnlyList<AppointmentCalendarItemDto> items,
        CancellationToken ct)
    {
        if (items.Count == 0)
            return items;

        var details = (await _appointments.ListAsync(
                new AppointmentCalendarDetailsByIdsSpec(tenantId, items.Select(i => i.Id).ToArray()), ct))
            .ToDictionary(r => r.AppointmentId);

        return await WithNamesAsync(
            tenantId,
            items.Select(i => details.TryGetValue(i.Id, out var d)
                    ? i with { VeterinarianUserId = d.VeterinarianUserId, ReasonId = d.ReasonId, Notes = d.Notes }
                    : i with { VeterinarianUserId = null })
                .ToList(),
            ct);
    }

    /// <summary>Hekim ve randevu nedeni adları.</summary>
    private async Task<IReadOnlyList<AppointmentCalendarItemDto>> WithNamesAsync(
        Guid tenantId,
        IReadOnlyList<AppointmentCalendarItemDto> items,
        CancellationToken ct)
    {
        var vetIds = items.Where(i => i.VeterinarianUserId is not null).Select(i => i.VeterinarianUserId!.Value).Distinct().ToArray();
        var reasonIds = items.Where(i => i.ReasonId is not null).Select(i => i.ReasonId!.Value).Distinct().ToArray();
        if (vetIds.Length == 0 && reasonIds.Length == 0)
            return items;

        IReadOnlyDictionary<Guid, string?> vetNames = vetIds.Length == 0 ? new Dictionary<Guid, string?>() : await _veterinarians.GetNamesAsync(vetIds, ct);
        var reasonNames = reasonIds.Length == 0
            ? new Dictionary<Guid, string>()
            : (await _reasons.ListAsync(new AppointmentReasonNamesByIdsSpec(tenantId, reasonIds), ct)).ToDictionary(r => r.Id, r => r.Name);

        return items
            .Select(i => i with
            {
                VeterinarianName = i.VeterinarianUserId is { } v && vetNames.TryGetValue(v, out var vetName) ? vetName : i.VeterinarianName,
                ReasonName = i.ReasonId is { } r && reasonNames.TryGetValue(r, out var reasonName) ? reasonName : i.ReasonName,
            })
            .ToList();
    }

    /// <summary>Takvim durumu (§8.3); kural tek yer <see cref="CalendarItemStateRule.ForAppointment"/> (filtre ve özetle aynı).</summary>
    private static IReadOnlyList<AppointmentCalendarItemDto> WithState(IReadOnlyList<AppointmentCalendarItemDto> items, DateTime utcNow)
        => items.Select(i => i with { State = CalendarItemStateRule.ForAppointment(i.Status, i.ScheduledAtUtc, utcNow) }).ToList();

    private static DateTime NormalizeToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
