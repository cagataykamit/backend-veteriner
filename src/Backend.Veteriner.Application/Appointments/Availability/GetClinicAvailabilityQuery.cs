using Backend.Veteriner.Application.Appointments.Reasons;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Appointments.Availability;

/// <summary>Personel görünümü: müsait saat ve o saatte boş uygun hekimler (talep onayında farklı saat seçimi).</summary>
public sealed record StaffAvailabilitySlotDto(DateTime StartUtc, DateTime EndUtc, IReadOnlyList<Guid> FreeVeterinarianUserIds);

public sealed record StaffAvailabilityDayDto(DateOnly Date, IReadOnlyList<StaffAvailabilitySlotDto> Slots);

/// <summary>Sahip görünümü: yalnız saat (hekim bilgisi yok).</summary>
public sealed record OnlineAvailabilitySlotDto(DateTime StartUtc, DateTime EndUtc);

public sealed record OnlineAvailabilityDayDto(DateOnly Date, IReadOnlyList<OnlineAvailabilitySlotDto> Slots);

public static class AvailabilityMapping
{
    public static IReadOnlyList<OnlineAvailabilityDayDto> ToOnline(IReadOnlyList<AvailabilityDay> days)
        => days.Select(d => new OnlineAvailabilityDayDto(
                d.LocalDate,
                d.Slots.Select(s => new OnlineAvailabilitySlotDto(s.StartUtc, s.EndUtc)).ToList()))
            .ToList();
}

/// <summary>
/// Kliniğin bir neden için müsait saatleri (personel; ADR-042 K3). <paramref name="From"/> İstanbul takvim günü (boşsa bugün),
/// en fazla 14 gün. Personel için online talep sınırları uygulanmaz; geçmiş saatler dönmez. Neden boşsa "Diğer / emin
/// değilim" talebinin onayı: süre ve hekimler online uçlardaki kuralla aynı (ADR-042 ek md. 1).
/// </summary>
public sealed record GetClinicAvailabilityQuery(Guid ClinicId, Guid? ReasonId, DateOnly? From, int Days)
    : IRequest<Result<IReadOnlyList<StaffAvailabilityDayDto>>>;

public sealed class GetClinicAvailabilityQueryValidator : AbstractValidator<GetClinicAvailabilityQuery>
{
    public GetClinicAvailabilityQueryValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.ReasonId).Must(id => id is null || id != Guid.Empty);
        RuleFor(x => x.Days).InclusiveBetween(1, AppointmentAvailabilityCalculator.MaxDays);
    }
}

public sealed class GetClinicAvailabilityQueryHandler
    : IRequestHandler<GetClinicAvailabilityQuery, Result<IReadOnlyList<StaffAvailabilityDayDto>>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IReadRepository<AppointmentReason> _reasons;
    private readonly AppointmentAvailabilityService _availability;
    private readonly TimeProvider _timeProvider;

    public GetClinicAvailabilityQueryHandler(
        ClinicDefinitionAccess access,
        IReadRepository<AppointmentReason> reasons,
        AppointmentAvailabilityService availability,
        TimeProvider timeProvider)
    {
        _access = access;
        _reasons = reasons;
        _availability = availability;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<IReadOnlyList<StaffAvailabilityDayDto>>> Handle(GetClinicAvailabilityQuery request, CancellationToken ct)
    {
        var access = await _access.EnsureReadAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<IReadOnlyList<StaffAvailabilityDayDto>>.Failure(access.Error);
        var tenantId = access.Value;

        AppointmentReason? reason = null;
        if (request.ReasonId is { } reasonId)
        {
            reason = await _reasons.FirstOrDefaultAsync(new AppointmentReasonByIdSpec(tenantId, request.ClinicId, reasonId), ct);
            if (reason is null)
                return Result<IReadOnlyList<StaffAvailabilityDayDto>>.Failure("AppointmentReasons.NotFound", "Randevu nedeni bulunamadı.");
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var from = request.From ?? OperationDayBounds.ToLocalDate(utcNow);
        var days = await _availability.GetDaysAsync(
            tenantId, request.ClinicId, reason, AppointmentRequest.DurationFor(reason), from, request.Days, utcNow, DateTime.MaxValue, ct);

        IReadOnlyList<StaffAvailabilityDayDto> dto = days
            .Select(d => new StaffAvailabilityDayDto(
                d.LocalDate,
                d.Slots.Select(s => new StaffAvailabilitySlotDto(s.StartUtc, s.EndUtc, s.FreeVeterinarianIds)).ToList()))
            .ToList();
        return Result<IReadOnlyList<StaffAvailabilityDayDto>>.Success(dto);
    }
}
