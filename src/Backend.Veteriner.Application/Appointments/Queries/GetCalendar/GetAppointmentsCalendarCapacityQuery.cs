using Backend.Veteriner.Application.Appointments.Availability;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Appointments.Queries.GetCalendar;

/// <summary>Bir günün randevu kapasitesi (APPOINTMENT_CALENDAR_API_CONTRACT.md §8.4); kapalı gün 0.</summary>
public sealed record CalendarDayCapacityDto(DateOnly Date, int Capacity);

/// <summary>
/// Takvim ay görünümü doluluğu: aralıktaki her İstanbul günü için kaç randevu sığar. Aralık ve klinik kapsamı
/// <see cref="GetAppointmentsCalendarQuery"/> ile aynı; <paramref name="VeterinarianUserId"/> verilirse tek hekimin kapasitesi.
/// Hesap <see cref="AppointmentAvailabilityCalculator.DailyCapacity"/>.
/// </summary>
public sealed record GetAppointmentsCalendarCapacityQuery(
    DateTime? DateFromUtc,
    DateTime? DateToUtc,
    Guid? ClinicId = null,
    Guid? VeterinarianUserId = null)
    : IRequest<Result<IReadOnlyList<CalendarDayCapacityDto>>>;

public sealed class GetAppointmentsCalendarCapacityQueryValidator : AbstractValidator<GetAppointmentsCalendarCapacityQuery>
{
    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(45);

    public GetAppointmentsCalendarCapacityQueryValidator()
    {
        RuleFor(x => x.DateFromUtc).NotNull().WithMessage("dateFromUtc zorunludur.");
        RuleFor(x => x.DateToUtc).NotNull().WithMessage("dateToUtc zorunludur.");
        RuleFor(x => x)
            .Must(q => !q.DateFromUtc.HasValue || !q.DateToUtc.HasValue || q.DateToUtc.Value > q.DateFromUtc.Value)
            .WithMessage("dateToUtc, dateFromUtc'den büyük olmalıdır.");
        RuleFor(x => x)
            .Must(q => !q.DateFromUtc.HasValue || !q.DateToUtc.HasValue || (q.DateToUtc.Value - q.DateFromUtc.Value) <= MaxWindow)
            .WithMessage("Tarih aralığı en fazla 45 gün olabilir.");
    }
}

public sealed class GetAppointmentsCalendarCapacityQueryHandler
    : IRequestHandler<GetAppointmentsCalendarCapacityQuery, Result<IReadOnlyList<CalendarDayCapacityDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly AppointmentAvailabilityService _availability;

    public GetAppointmentsCalendarCapacityQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        AppointmentAvailabilityService availability)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _availability = availability;
    }

    public async ValueTask<Result<IReadOnlyList<CalendarDayCapacityDto>>> Handle(GetAppointmentsCalendarCapacityQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<IReadOnlyList<CalendarDayCapacityDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (request.DateFromUtc is not { } from || request.DateToUtc is not { } to || to <= from)
        {
            return Result<IReadOnlyList<CalendarDayCapacityDto>>.Failure(
                "Appointments.Calendar.InvalidDateWindow",
                "dateFromUtc ve dateToUtc zorunludur; dateToUtc büyük olmalıdır.");
        }

        if (ClinicContextRule.Conflicts(request.ClinicId, _clinicContext.ClinicId))
            return Result<IReadOnlyList<CalendarDayCapacityDto>>.Failure(ClinicContextRule.Mismatch("Appointments"));

        var requestedClinicId = request.ClinicId ?? _clinicContext.ClinicId;
        if (requestedClinicId is null)
        {
            return Result<IReadOnlyList<CalendarDayCapacityDto>>.Failure(
                "Appointments.ClinicScopeRequired",
                "Klinik kapsamı gerekli: aktif klinik bağlamı yok ve clinicId belirtilmedi. Takvim klinik kapsamı olmadan listelenemez.");
        }

        var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, requestedClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<IReadOnlyList<CalendarDayCapacityDto>>.Failure(scopeResult.Error);

        var effectiveClinicId = scopeResult.Value!.SingleClinicId ?? requestedClinicId.Value;
        var fromUtc = ToUtc(from);
        var toUtc = ToUtc(to);
        var fromDate = OperationDayBounds.ToLocalDate(fromUtc);
        var days = 0;
        while (OperationDayBounds.ForLocalDate(fromDate.AddDays(days)).DayStartUtc < toUtc)
            days++;

        var capacity = await _availability.GetDailyCapacityAsync(
            tenantId, effectiveClinicId, request.VeterinarianUserId is not null, fromDate, days, ct);
        return Result<IReadOnlyList<CalendarDayCapacityDto>>.Success(
            capacity.Select(c => new CalendarDayCapacityDto(c.Date, c.Capacity)).ToList());
    }

    private static DateTime ToUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
