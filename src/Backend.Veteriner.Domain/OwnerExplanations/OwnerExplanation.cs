using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.OwnerExplanations;

/// <summary>
/// Muayeneye bağlı, hasta sahibine verilecek sade dilde açıklama (AI-003). Muayene kaydının parçası değildir; kesinleşmiş
/// muayeneye de eklenebilir. Bugün yalnız AI taslağının kabulüyle oluşur (<see cref="SourceAiDraftId"/>); oluşturulduktan
/// sonra değişmez (düzeltme yeni açıklamayla yapılır).
/// </summary>
public sealed class OwnerExplanation : AggregateRoot, IClinicScoped, ITenantOwned
{
    public const int MaxTextLength = 4000;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid PetId { get; private set; }
    public Guid ExaminationId { get; private set; }
    public string Text { get; private set; } = default!;

    /// <summary>Metnin başladığı AI taslağı; elle yazılan açıklamada <c>null</c>.</summary>
    public Guid? SourceAiDraftId { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private OwnerExplanation() { }

    public static Result<OwnerExplanation> Create(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid examinationId,
        string? text,
        Guid? sourceAiDraftId,
        Guid createdByUserId,
        DateTime utcNow)
    {
        if (tenantId == Guid.Empty || clinicId == Guid.Empty || petId == Guid.Empty || examinationId == Guid.Empty)
            return Result<OwnerExplanation>.Failure("OwnerExplanations.Validation", "Muayene bağlantısı geçersiz.");
        if (createdByUserId == Guid.Empty)
            return Result<OwnerExplanation>.Failure("OwnerExplanations.Validation", "Oluşturan kullanıcı geçersiz.");
        if (sourceAiDraftId == Guid.Empty)
            return Result<OwnerExplanation>.Failure("OwnerExplanations.Validation", "Kaynak taslak geçersiz.");

        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxTextLength)
        {
            return Result<OwnerExplanation>.Failure(
                "OwnerExplanations.Validation",
                $"Açıklama metni 1-{MaxTextLength} karakter olmalıdır.");
        }

        return Result<OwnerExplanation>.Success(new OwnerExplanation
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            PetId = petId,
            ExaminationId = examinationId,
            Text = trimmed,
            SourceAiDraftId = sourceAiDraftId,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = utcNow,
        });
    }
}
