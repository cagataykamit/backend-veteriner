using Backend.Veteriner.Application.Common.Time;

namespace Backend.Veteriner.Application.Dashboard.ReadModels;

/// <summary>
/// Dashboard finance read-model sorgusu; scope handler tarafında çözülür.
/// </summary>
public sealed record DashboardFinanceReadRequest(
    Guid TenantId,
    Guid? ClinicId,
    DateOnly TodayLocalDate,
    IReadOnlyList<DateOnly> WeekLocalDatesInclusive,
    IReadOnlyList<DateOnly> MonthLocalDatesInclusive,
    IReadOnlyList<OperationPeriodBounds.DailyWindow> LastSevenDayBuckets);
