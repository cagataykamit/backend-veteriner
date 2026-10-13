using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.CareInstructions;

/// <summary>
/// Hasta sahibine verilecek taburcu / evde bakım talimatı. Yatışa, muayeneye ve/veya geliş (visit) kaydına
/// opsiyonel bağlanır (ayaktan hasta için de yazılabilir); bağlar oluşturulduktan sonra değişmez, yalnız metin düzenlenir.
/// </summary>
public sealed class CareInstruction : AggregateRoot
{
    public const int MaxTextLength = 4000;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid PetId { get; private set; }
    public Guid? HospitalizationId { get; private set; }
    public Guid? ExaminationId { get; private set; }
    public Guid? VisitId { get; private set; }
    public string Text { get; private set; } = default!;
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>SQL Server rowversion — eşzamanlı güncelleme için.</summary>
    public byte[] RowVersion { get; private set; } = default!;

    private CareInstruction() { }

    public CareInstruction(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid? hospitalizationId,
        Guid? examinationId,
        Guid? visitId,
        Guid createdByUserId,
        string text)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is invalid.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId is invalid.", nameof(clinicId));
        if (petId == Guid.Empty)
            throw new ArgumentException("PetId is invalid.", nameof(petId));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("CreatedByUserId is invalid.", nameof(createdByUserId));

        var check = ValidateText(text);
        if (!check.IsSuccess)
            throw new ArgumentException(check.Error.Message, nameof(text));

        TenantId = tenantId;
        ClinicId = clinicId;
        PetId = petId;
        HospitalizationId = hospitalizationId;
        ExaminationId = examinationId;
        VisitId = visitId;
        CreatedByUserId = createdByUserId;
        Text = text.Trim();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Result UpdateText(Guid updatedByUserId, string text)
    {
        if (updatedByUserId == Guid.Empty)
            return Result.Failure("CareInstructions.Validation", "UpdatedByUserId is invalid.");

        var check = ValidateText(text);
        if (!check.IsSuccess)
            return check;

        Text = text.Trim();
        UpdatedByUserId = updatedByUserId;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// İstemcinin gördüğü sürüm: kalıcı yazma yalnızca veritabanındaki sürüm bu değere eşitse gerçekleşir.
    /// </summary>
    public void SetExpectedRowVersion(byte[] expectedRowVersion)
    {
        if (expectedRowVersion is null || expectedRowVersion.Length == 0)
            throw new ArgumentException("RowVersion geçersiz.", nameof(expectedRowVersion));

        RowVersion = expectedRowVersion;
    }

    private static Result ValidateText(string? text)
    {
        var length = text?.Trim().Length ?? 0;
        return length is >= 1 and <= MaxTextLength
            ? Result.Success()
            : Result.Failure("CareInstructions.Validation", $"Talimat metni 1-{MaxTextLength} karakter olmalıdır.");
    }
}
