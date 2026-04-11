namespace Backend.Veteriner.Api.Contracts;

public sealed class ScheduleSubscriptionDowngradeBody
{
    public string TargetPlanCode { get; set; } = default!;
    public string? Reason { get; set; }
}
