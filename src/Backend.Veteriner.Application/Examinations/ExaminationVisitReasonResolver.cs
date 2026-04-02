namespace Backend.Veteriner.Application.Examinations;

/// <summary>
/// API gövdesinde kanonik alan <c>visitReason</c>; legacy <c>complaint</c> yalnızca geriye dönük uyumluluk.
/// İkisi de doluysa <c>visitReason</c> önceliklidir.
/// </summary>
public static class ExaminationVisitReasonResolver
{
    public static string Resolve(string? visitReason, string? complaint)
    {
        if (!string.IsNullOrWhiteSpace(visitReason))
            return visitReason.Trim();
        if (!string.IsNullOrWhiteSpace(complaint))
            return complaint.Trim();
        return string.Empty;
    }
}
