namespace Backend.Veteriner.Application.Common.Clinic;

/// <summary>
/// GET <c>/reminders/logs</c> üzerindeki <c>clinicId</c> sorgu parametresi veri filtresidir;
/// JWT seçili klinik ile birleştirilmez (<see cref="ClinicRequestResolver"/>).
/// </summary>
public static class ReminderDispatchLogsClinicContextRules
{
    public static bool ShouldIgnoreQueryClinicIdForResolver(string httpMethod, string? path)
    {
        if (!string.Equals(httpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrEmpty(path))
            return false;

        var normalized = path.TrimEnd('/');
        return normalized.EndsWith("/reminders/logs", StringComparison.OrdinalIgnoreCase);
    }
}
