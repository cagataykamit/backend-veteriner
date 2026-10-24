using System.Linq.Expressions;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.LabResults;

/// <summary>
/// Hayvan için lab kaydı: önce istenir (sonuçsuz), sonra sonuçlanır; yanlış kayıt gerekçeyle iptal edilir.
/// İsteğe bağlı olarak muayeneye ve geliş (visit) kaydına bağlanır.
/// </summary>
public sealed class LabResult : AggregateRoot, IClinicScoped
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid PetId { get; private set; }
    public Guid? ExaminationId { get; private set; }
    public Guid? VisitId { get; private set; }
    public LabResultStatus Status { get; private set; }

    /// <summary>
    /// Önce istenip sonra sonuçlanan kayıtta sunucunun istek anıdır; doğrudan sonuçlu kayıtta girilen sonuç tarihidir
    /// (klinik gerçek tarihe göre sıralansın). Eski kayıtlarda sonuç tarihine eşitlenmiştir.
    /// </summary>
    public DateTime RequestedAtUtc { get; private set; }
    public Guid? RequestedByUserId { get; private set; }

    /// <summary>Yalnız <see cref="LabResultStatus.Requested"/> durumunda boştur; <see cref="ResultText"/> ile birlikte dolar.</summary>
    public DateTime? ResultDateUtc { get; private set; }
    public string TestName { get; private set; } = default!;
    public string? ResultText { get; private set; }
    public string? Interpretation { get; private set; }
    public string? Notes { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public string? CancelReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>SQL Server rowversion — eşzamanlı güncelleme için.</summary>
    public byte[] RowVersion { get; private set; } = default!;

    /// <summary>Yeni kayıtlarda true; yorum gerekmiyor sayılan eski (migration ile gelen) kayıtlarda false.</summary>
    public bool InterpretationRequired { get; private set; }

    /// <summary>
    /// Sonuç var, yorum gerekiyor ama hekim yorumu yok (tek kural): sorgularda bu ifade, bellekte
    /// <see cref="AwaitingInterpretation"/> kullanılır.
    /// </summary>
    public static readonly Expression<Func<LabResult, bool>> IsAwaitingInterpretation =
        l => l.Status == LabResultStatus.Resulted && l.InterpretationRequired && l.Interpretation == null;

    private static readonly Func<LabResult, bool> IsAwaitingInterpretationCompiled = IsAwaitingInterpretation.Compile();

    /// <summary>Durum makinesine girmeyen türetilmiş bayrak; kural <see cref="IsAwaitingInterpretation"/>.</summary>
    public bool AwaitingInterpretation => IsAwaitingInterpretationCompiled(this);

    private LabResult() { }

    /// <summary>Sonuç alanları verilmezse kayıt <see cref="LabResultStatus.Requested"/>, verilirse <see cref="LabResultStatus.Resulted"/> olur.</summary>
    public LabResult(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid? examinationId,
        Guid? visitId,
        Guid? requestedByUserId,
        DateTime? resultDateUtc,
        string testName,
        string? resultText,
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

        var resultCheck = ValidateResult(resultDateUtc, resultText, interpretation);
        if (!resultCheck.IsSuccess)
            throw new ArgumentException(resultCheck.Error.Message, nameof(resultText));

        var hasResult = !string.IsNullOrWhiteSpace(resultText);
        var now = DateTime.UtcNow;

        TenantId = tenantId;
        ClinicId = clinicId;
        PetId = petId;
        ExaminationId = examinationId;
        VisitId = visitId;
        Status = hasResult ? LabResultStatus.Resulted : LabResultStatus.Requested;
        InterpretationRequired = true;
        ResultDateUtc = hasResult ? NormalizeUtc(resultDateUtc!.Value) : null;
        RequestedAtUtc = ResultDateUtc ?? now;
        RequestedByUserId = requestedByUserId;
        TestName = testName.Trim();
        ResultText = hasResult ? resultText!.Trim() : null;
        Interpretation = Trimmed(interpretation);
        Notes = Trimmed(notes);
        CreatedAtUtc = now;
        UpdatedAtUtc = null;
    }

    /// <summary>
    /// Tam güncelleme. <see cref="LabResultStatus.Requested"/> kayda sonuç yazılırsa kayıt sonuçlanır;
    /// sonuçlanmış kayıttan sonuç silinemez. İptal edilmiş kayıt değişmez.
    /// </summary>
    public Result UpdateDetails(
        Guid clinicId,
        Guid petId,
        Guid? examinationId,
        Guid? visitId,
        DateTime? resultDateUtc,
        string testName,
        string? resultText,
        string? interpretation,
        string? notes)
    {
        if (Status == LabResultStatus.Cancelled)
            return AlreadyCancelled();
        if (clinicId == Guid.Empty)
            return Result.Failure("LabResults.Validation", "ClinicId is invalid.");
        if (petId == Guid.Empty)
            return Result.Failure("LabResults.Validation", "PetId is invalid.");
        if (string.IsNullOrWhiteSpace(testName))
            return Result.Failure("LabResults.Validation", "TestName is required.");

        var resultCheck = ValidateResult(resultDateUtc, resultText, interpretation);
        if (!resultCheck.IsSuccess)
            return resultCheck;

        var hasResult = !string.IsNullOrWhiteSpace(resultText);
        if (Status == LabResultStatus.Resulted && !hasResult)
        {
            return Result.Failure(
                "LabResults.Validation",
                "Sonuçlanmış kaydın sonucu silinemez; yanlış kayıt iptal edilmelidir.");
        }

        ClinicId = clinicId;
        PetId = petId;
        ExaminationId = examinationId;
        VisitId = visitId;
        if (hasResult)
            Status = LabResultStatus.Resulted;
        ResultDateUtc = hasResult ? NormalizeUtc(resultDateUtc!.Value) : null;
        TestName = testName.Trim();
        ResultText = hasResult ? resultText!.Trim() : null;
        Interpretation = Trimmed(interpretation);
        Notes = Trimmed(notes);
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Kaydı gerekçeyle iptal eder (silmez). Muayene kilidi denetimi çağıranındır.</summary>
    public Result Cancel(Guid cancelledByUserId, string? reason)
    {
        if (Status == LabResultStatus.Cancelled)
            return AlreadyCancelled();
        if (cancelledByUserId == Guid.Empty)
            return Result.Failure("LabResults.Validation", "İptal eden kullanıcı geçersiz.");

        var reasonCheck = CorrectionReason.Validate(reason, "LabResults.Validation");
        if (!reasonCheck.IsSuccess)
            return reasonCheck;

        var now = DateTime.UtcNow;
        Status = LabResultStatus.Cancelled;
        CancelledAtUtc = now;
        CancelledByUserId = cancelledByUserId;
        CancelReason = reason!.Trim();
        UpdatedAtUtc = now;
        return Result.Success();
    }

    /// <summary>Test adı, hayvan, klinik veya muayene bağı değişiyor mu; sonuç alanları (tarih, metin, yorum, not) kimlik sayılmaz.</summary>
    public bool ChangesIdentity(Guid clinicId, Guid petId, Guid? examinationId, string testName)
        => ClinicId != clinicId
           || PetId != petId
           || ExaminationId != examinationId
           || !string.Equals(TestName, testName?.Trim(), StringComparison.Ordinal);

    /// <summary>
    /// İstemcinin gördüğü sürüm: kalıcı yazma yalnızca veritabanındaki sürüm bu değere eşitse gerçekleşir.
    /// </summary>
    public void SetExpectedRowVersion(byte[] expectedRowVersion)
    {
        if (expectedRowVersion is null || expectedRowVersion.Length == 0)
            throw new ArgumentException("RowVersion geçersiz.", nameof(expectedRowVersion));

        RowVersion = expectedRowVersion;
    }

    /// <summary>Sonuç metni ile tarihi birlikte verilir/verilmez; sonuç yokken yorum yazılamaz.</summary>
    public static Result ValidateResult(DateTime? resultDateUtc, string? resultText, string? interpretation)
    {
        var hasText = !string.IsNullOrWhiteSpace(resultText);
        if (hasText != resultDateUtc.HasValue)
            return Result.Failure("LabResults.Validation", "Sonuç metni ve sonuç tarihi birlikte verilmelidir.");
        if (!hasText && !string.IsNullOrWhiteSpace(interpretation))
            return Result.Failure("LabResults.Validation", "Sonuç girilmeden yorum yazılamaz.");

        return Result.Success();
    }

    private static Result AlreadyCancelled()
        => Result.Failure("LabResults.AlreadyCancelled", "Laboratuvar kaydı iptal edilmiş; değiştirilemez.");

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
