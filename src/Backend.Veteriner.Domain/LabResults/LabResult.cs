using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.LabResults;

/// <summary>
/// Clinical laboratory result for a pet; optionally linked to an examination.
/// </summary>
public sealed class LabResult : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid PetId { get; private set; }
    public Guid? ExaminationId { get; private set; }
    public DateTime ResultDateUtc { get; private set; }
    public string TestName { get; private set; } = default!;
    public string ResultText { get; private set; } = default!;
    public string? Interpretation { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private LabResult() { }

    public LabResult(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid? examinationId,
        DateTime resultDateUtc,
        string testName,
        string resultText,
        string? interpretation,
        string? notes)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is invalid.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId is invalid.", nameof(clinicId));
        if (petId == Guid.Empty)
            throw new ArgumentException("PetId is invalid.", nameof(petId));
        if (string.IsNullOrWhiteSpace(testName))
            throw new ArgumentException("TestName is required.", nameof(testName));
        if (string.IsNullOrWhiteSpace(resultText))
            throw new ArgumentException("ResultText is required.", nameof(resultText));

        TenantId = tenantId;
        ClinicId = clinicId;
        PetId = petId;
        ExaminationId = examinationId;
        ResultDateUtc = NormalizeUtc(resultDateUtc);
        TestName = testName.Trim();
        ResultText = resultText.Trim();
        Interpretation = string.IsNullOrWhiteSpace(interpretation) ? null : interpretation.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = null;
    }

    public Result UpdateDetails(
        Guid clinicId,
        Guid petId,
        Guid? examinationId,
        DateTime resultDateUtc,
        string testName,
        string resultText,
        string? interpretation,
        string? notes)
    {
        if (clinicId == Guid.Empty)
            return Result.Failure("LabResults.Validation", "ClinicId is invalid.");
        if (petId == Guid.Empty)
            return Result.Failure("LabResults.Validation", "PetId is invalid.");
        if (string.IsNullOrWhiteSpace(testName))
            return Result.Failure("LabResults.Validation", "TestName is required.");
        if (string.IsNullOrWhiteSpace(resultText))
            return Result.Failure("LabResults.Validation", "ResultText is required.");

        ClinicId = clinicId;
        PetId = petId;
        ExaminationId = examinationId;
        ResultDateUtc = NormalizeUtc(resultDateUtc);
        TestName = testName.Trim();
        ResultText = resultText.Trim();
        Interpretation = string.IsNullOrWhiteSpace(interpretation) ? null : interpretation.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
