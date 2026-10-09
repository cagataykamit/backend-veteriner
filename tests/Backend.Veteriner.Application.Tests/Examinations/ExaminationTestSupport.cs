using Backend.Veteriner.Domain.Examinations;

namespace Backend.Veteriner.Application.Tests.Examinations;

internal static class ExaminationTestSupport
{
    public static readonly byte[] SampleRowVersionBytes = [1, 0, 0, 0, 0, 0, 0, 1];

    public static string SampleRowVersionBase64 => Convert.ToBase64String(SampleRowVersionBytes);

    public static void SetRowVersion(Examination examination, byte[]? bytes = null)
    {
        typeof(Examination).GetProperty(nameof(Examination.RowVersion))!
            .SetValue(examination, bytes ?? SampleRowVersionBytes);
    }
}
