using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Application.Dashboard.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using MediatR;

namespace Backend.Veteriner.Application.Dashboard.Queries.GetOperationalAlerts;

public sealed class GetDashboardOperationalAlertsQueryHandler
    : IRequestHandler<GetDashboardOperationalAlertsQuery, Result<DashboardOperationalAlertsDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly IDashboardTodayAppointmentStatusCountsReader _todayAppointmentCounts;

    public GetDashboardOperationalAlertsQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Appointment> appointments,
        IReadRepository<Vaccination> vaccinations,
        IDashboardTodayAppointmentStatusCountsReader todayAppointmentCounts)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _appointments = appointments;
        _vaccinations = vaccinations;
        _todayAppointmentCounts = todayAppointmentCounts;
    }

    public async Task<Result<DashboardOperationalAlertsDto>> Handle(GetDashboardOperationalAlertsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<DashboardOperationalAlertsDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var nowUtc = DateTime.UtcNow;
        var next24HoursUtcExclusive = nowUtc.AddHours(24);
        var next7DaysUtcExclusive = nowUtc.AddDays(7);
        var clinicId = _clinicContext.ClinicId;
        var (dayStartUtc, dayEndUtc) = OperationDayBounds.ForUtcNow(nowUtc);

        var todayCounts = await _todayAppointmentCounts.GetAsync(tenantId, clinicId, dayStartUtc, dayEndUtc, ct);
        var overdueScheduledAppointmentsCount = await _appointments.CountAsync(
            new DashboardOverdueScheduledAppointmentsCountSpec(tenantId, clinicId, nowUtc), ct);
        var upcomingAppointmentsNext24HoursCount = await _appointments.CountAsync(
            new DashboardUpcomingAppointmentsNext24HoursCountSpec(tenantId, clinicId, nowUtc, next24HoursUtcExclusive), ct);
        var overdueVaccinationsCount = await _vaccinations.CountAsync(
            new DashboardOverdueVaccinationsCountSpec(tenantId, clinicId, nowUtc), ct);
        var upcomingVaccinationsNext7DaysCount = await _vaccinations.CountAsync(
            new DashboardUpcomingVaccinationsNext7DaysCountSpec(tenantId, clinicId, nowUtc, next7DaysUtcExclusive), ct);

        var dto = new DashboardOperationalAlertsDto(
            OverdueScheduledAppointmentsCount: overdueScheduledAppointmentsCount,
            UpcomingAppointmentsNext24HoursCount: upcomingAppointmentsNext24HoursCount,
            TodayCancelledAppointmentsCount: todayCounts.Cancelled,
            OverdueVaccinationsCount: overdueVaccinationsCount,
            UpcomingVaccinationsNext7DaysCount: upcomingVaccinationsNext7DaysCount);

        return Result<DashboardOperationalAlertsDto>.Success(dto);
    }
}
