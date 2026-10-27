using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Dashboard.Contracts;
using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Application.Dashboard.ReadModels;
using Backend.Veteriner.Application.Dashboard.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Vaccinations;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Backend.Veteriner.Application.Dashboard.Queries.GetOperationalAlerts;

public sealed class GetDashboardOperationalAlertsQueryHandler
    : IRequestHandler<GetDashboardOperationalAlertsQuery, Result<DashboardOperationalAlertsDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly IReadRepository<Pet> _pets;
    private readonly IDashboardTodayAppointmentStatusCountsReader _todayAppointmentCounts;
    private readonly TimeProvider _timeProvider;

    public GetDashboardOperationalAlertsQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Appointment> appointments,
        IReadRepository<Vaccination> vaccinations,
        IReadRepository<Pet> pets,
        IDashboardTodayAppointmentStatusCountsReader todayAppointmentCounts,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _appointments = appointments;
        _vaccinations = vaccinations;
        _pets = pets;
        _todayAppointmentCounts = todayAppointmentCounts;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<DashboardOperationalAlertsDto>> Handle(GetDashboardOperationalAlertsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<DashboardOperationalAlertsDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, _clinicContext.ClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<DashboardOperationalAlertsDto>.Failure(scopeResult.Error);

        var singleClinicId = scopeResult.Value!.SingleClinicId;
        var accessibleClinicIds = scopeResult.Value.AccessibleClinicIds;

        if (accessibleClinicIds is { Count: 0 })
        {
            return Result<DashboardOperationalAlertsDto>.Success(
                new DashboardOperationalAlertsDto(0, 0, 0, 0, 0));
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var next24HoursUtcExclusive = nowUtc.AddHours(24);
        var (dayStartUtc, dayEndUtc) = OperationDayBounds.ForUtcNow(nowUtc);
        // Aşı gecikmiş/yaklaşan tanımı gün bazlıdır (ADR-028); randevu sayaçları saat bazlı kalır.
        var overdueVaccinationsBeforeUtc = VaccinationDueRule.OverdueBeforeUtc(nowUtc);
        var (upcomingVaccinationsFromUtc, upcomingVaccinationsToUtcExclusive) =
            VaccinationDueRule.UpcomingWindow(nowUtc, VaccinationDueRule.DashboardUpcomingDays);

        var todayCounts = await _todayAppointmentCounts.GetAsync(
            tenantId, singleClinicId, dayStartUtc, dayEndUtc, accessibleClinicIds, ct);
        // Vefat eden hayvanın aşısı gecikmiş/yaklaşan, açık kalan planlı randevusu gecikmiş sayılmaz (ADR-020 S5).
        var petIdsNotReceivingCare = await _pets.ListAsync(new PetIdsNotReceivingCareSpec(tenantId), ct);
        var overdueScheduledAppointmentsCount = await _appointments.CountAsync(
            new DashboardOverdueScheduledAppointmentsCountSpec(
                tenantId, singleClinicId, nowUtc, accessibleClinicIds, petIdsNotReceivingCare), ct);
        var upcomingAppointmentsNext24HoursCount = await _appointments.CountAsync(
            new DashboardUpcomingAppointmentsNext24HoursCountSpec(
                tenantId, singleClinicId, nowUtc, next24HoursUtcExclusive, accessibleClinicIds), ct);
        var overdueVaccinationsCount = await _vaccinations.CountAsync(
            new DashboardOverdueVaccinationsCountSpec(
                tenantId, singleClinicId, overdueVaccinationsBeforeUtc, accessibleClinicIds, petIdsNotReceivingCare), ct);
        var upcomingVaccinationsNext7DaysCount = await _vaccinations.CountAsync(
            new DashboardUpcomingVaccinationsNext7DaysCountSpec(
                tenantId,
                singleClinicId,
                upcomingVaccinationsFromUtc,
                upcomingVaccinationsToUtcExclusive,
                accessibleClinicIds,
                petIdsNotReceivingCare), ct);

        var dto = new DashboardOperationalAlertsDto(
            OverdueScheduledAppointmentsCount: overdueScheduledAppointmentsCount,
            UpcomingAppointmentsNext24HoursCount: upcomingAppointmentsNext24HoursCount,
            TodayCancelledAppointmentsCount: todayCounts.Cancelled,
            OverdueVaccinationsCount: overdueVaccinationsCount,
            UpcomingVaccinationsNext7DaysCount: upcomingVaccinationsNext7DaysCount);

        return Result<DashboardOperationalAlertsDto>.Success(dto);
    }
}
