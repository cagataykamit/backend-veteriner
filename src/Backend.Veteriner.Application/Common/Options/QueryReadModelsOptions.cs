namespace Backend.Veteriner.Application.Common.Options;

public sealed class QueryReadModelsOptions
{
    public const string SectionName = "QueryReadModels";

    public bool AppointmentsEnabled { get; set; }

    public bool DashboardAppointmentsEnabled { get; set; }
}
