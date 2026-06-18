using Backend.Veteriner.Application.Common.Options;

namespace Backend.Veteriner.Application.Projections.Appointments;

public static class AppointmentProjectionModeTags
{
    public const string CommandRead = "command-read";
    public const string AppointmentQuery = "appointment-query";
    public const string FullQuery = "full-query";

    public static string Resolve(QueryReadModelsOptions options)
    {
        if (options.AppointmentsEnabled && options.DashboardAppointmentsEnabled)
            return FullQuery;

        if (options.AppointmentsEnabled)
            return AppointmentQuery;

        return CommandRead;
    }
}
