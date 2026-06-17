using Backend.Veteriner.Application.Common.Time;

namespace Backend.Veteriner.Application.Dashboard.ReadModels;

/// <summary>
/// Dashboard appointment read-model sorgusu; scope handler tarafında çözülür.
/// </summary>
public sealed record DashboardAppointmentReadRequest(
    Guid TenantId,
    Guid? ClinicId,
    DateOnly TodayLocalDate,
    DateTime TodayStartUtc,
    DateTime TodayEndUtc,
    DateTime UpcomingCountFromUtcExclusive,
    DateTime UpcomingListFromUtcInclusive,
    int UpcomingListLimit,
    int RecentListLimit,
    IReadOnlyList<OperationPeriodBounds.DailyWindow> LastSevenDayBuckets);
