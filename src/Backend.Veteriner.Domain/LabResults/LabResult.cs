using System.Linq.Expressions;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.LabResults;

/// <summary>
/// Hayvan için lab kaydı: önce istenir (sonuçsuz), sonra sonuçlanır; yanlış kayıt gerekçeyle iptal edilir.
/// İsteğe bağlı olarak muayeneye ve geliş (visit) kaydına bağlanır.
/// </summary>
public sealed class LabResult : AggregateRoot, IClinicScoped, ITenantOwned
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

    /// <summary>Yalnız <see cref="LabResultStatus.Requested"/> durumunda boştur; sonuç (metin ya da değer satırı) ile birlikte dolar.</summary>
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

    /// <summary>
    /// Kesinleşmiş muayeneye sonradan eklenen tanısal kayıt (ADR-043 AK-4; görüntüleme deseni): gerekçe istenmez, rozet gösterilir.
    /// Yalnız oluşturmada işaretlenir (<see cref="MarkAddedAfterFinalization"/>).
    /// </summary>
    public bool AddedAfterFinalization { get; private set; }

    /// <summary>Yeni kayıtlarda true; yorum gerekmiyor sayılan eski (migration ile gelen) kayıtlarda false.</summary>
    public bool InterpretationRequired { get; private set; }

    private readonly List<LabResultLine> _lines = new();

    /// <summary>İsteğe bağlı yapılandırılmış değer satırları (REF-001 Dilim 3); serbest metin sonuç yanında kalır.</summary>
    public IReadOnlyList<LabResultLine> Lines => _lines;

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

    /// <summary>
    /// Sonuç (metin ya da değer satırı) verilmezse kayıt <see cref="LabResultStatus.Requested"/>, verilirse
    /// <see cref="LabResultStatus.Resulted"/> olur.
    /// </summary>
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
        string? notes,
        DateTime utcNow,
        IReadOnlyList<LabResultLineDraft>? lines = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is invalid.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId is invalid.", nameof(clinicId));
        if (petId == Guid.Empty)
            throw new ArgumentException("PetId is invalid.", nameof(petId));
        if (string.IsNullOrWhiteSpace(testName))
            throw new ArgumentException("TestName is required.", nameof(testName));

        lines ??= [];
        var resultCheck = ValidateResult(resultDateUtc, resultText, interpretation, lines);
        if (!resultCheck.IsSuccess)
            throw new ArgumentException(resultCheck.Error.Message, nameof(resultText));

        var hasResult = HasResult(resultText, lines.Count);

        TenantId = tenantId;
        ClinicId = clinicId;
        PetId = petId;
        ExaminationId = examinationId;
        VisitId = visitId;
        Status = hasResult ? LabResultStatus.Resulted : LabResultStatus.Requested;
        InterpretationRequired = true;
        ResultDateUtc = hasResult ? NormalizeUtc(resultDateUtc!.Value) : null;
        RequestedAtUtc = ResultDateUtc ?? utcNow;
        RequestedByUserId = requestedByUserId;
        TestName = testName.Trim();
        ResultText = Trimmed(resultText);
        Interpretation = Trimmed(interpretation);
        Notes = Trimmed(notes);
        CreatedAtUtc = utcNow;
        UpdatedAtUtc = null;
        SetLines(lines);
    }

    /// <summary>
    /// Tam güncelleme. <see cref="LabResultStatus.Requested"/> kayda sonuç yazılırsa kayıt sonuçlanır;
    /// sonuçlanmış kayıttan sonuç silinemez. İptal edilmiş kayıt değişmez. <paramref name="lines"/> null ise değer
    /// satırlarına dokunulmaz, boş liste hepsini siler, dolu liste satırları bu liste yapar.
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
        string? notes,
        DateTime utcNow,
        IReadOnlyList<LabResultLineDraft>? lines = null)
    {
        if (Status == LabResultStatus.Cancelled)
            return AlreadyCancelled();
        if (clinicId == Guid.Empty)
            return Result.Failure("LabResults.Validation", "Geçerli bir klinik seçin.");
        if (petId == Guid.Empty)
            return Result.Failure("LabResults.Validation", "Geçerli bir hasta seçin.");
        if (string.IsNullOrWhiteSpace(testName))
            return Result.Failure("LabResults.Validation", "Test adı zorunludur.");
        var subject = RecordSubjectRule.EnsureUnchanged(ClinicId, PetId, clinicId, petId, "LabResults");
        if (!subject.IsSuccess)
            return subject;

        var resultCheck = lines is null
            ? ValidateResult(resultDateUtc, resultText, interpretation, _lines.Count)
            : ValidateResult(resultDateUtc, resultText, interpretation, lines);
        if (!resultCheck.IsSuccess)
            return resultCheck;

        var hasResult = HasResult(resultText, lines?.Count ?? _lines.Count);
        if (Status == LabResultStatus.Resulted && !hasResult)
        {
            return Result.Failure(
                "LabResults.Validation",
                "Sonuçlanmış kaydın sonucu (metin ve değer satırları) silinemez; yanlış kayıt iptal edilmelidir.");
        }

        ClinicId = clinicId;
        PetId = petId;
        ExaminationId = examinationId;
        VisitId = visitId;
        if (hasResult)
            Status = LabResultStatus.Resulted;
        ResultDateUtc = hasResult ? NormalizeUtc(resultDateUtc!.Value) : null;
        TestName = testName.Trim();
        ResultText = Trimmed(resultText);
        Interpretation = Trimmed(interpretation);
        Notes = Trimmed(notes);
        UpdatedAtUtc = utcNow;
        if (lines is not null)
            SetLines(lines);
        return Result.Success();
    }

    /// <summary>Kaydı gerekçeyle iptal eder (silmez). Muayene kilidi denetimi çağıranındır.</summary>
    public Result Cancel(Guid cancelledByUserId, string? reason, DateTime utcNow)
    {
        if (Status == LabResultStatus.Cancelled)
            return AlreadyCancelled();
        if (cancelledByUserId == Guid.Empty)
            return Result.Failure("LabResults.Validation", "İptal eden kullanıcı geçersiz.");

        var reasonCheck = CorrectionReason.Validate(reason, "LabResults.Validation");
        if (!reasonCheck.IsSuccess)
            return reasonCheck;

        Status = LabResultStatus.Cancelled;
        CancelledAtUtc = utcNow;
        CancelledByUserId = cancelledByUserId;
        CancelReason = reason!.Trim();
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>Test adı, hayvan, klinik veya muayene bağı değişiyor mu; sonuç alanları (tarih, metin, yorum, not) kimlik sayılmaz.</summary>
    /// <summary>Bağlı muayene kayıt anında kesinleşmişse çağrılır; yalnız oluşturmada.</summary>
    public void MarkAddedAfterFinalization() => AddedAfterFinalization = true;

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

    /// <summary>
    /// Sonuç (metin ya da en az bir değer satırı) ile tarihi birlikte verilir/verilmez; sonuç yokken yorum yazılamaz.
    /// Satırlar tek tek ve birlikte (sayı sınırı, aynı parametre iki kez) doğrulanır.
    /// </summary>
    public static Result ValidateResult(
        DateTime? resultDateUtc,
        string? resultText,
        string? interpretation,
        IReadOnlyList<LabResultLineDraft>? lines = null)
    {
        lines ??= [];
        if (lines.Count > LabResultLine.MaxPerResult)
        {
            return Result.Failure(
                "LabResults.Validation",
                $"Bir lab sonucunda en fazla {LabResultLine.MaxPerResult} değer satırı olabilir.");
        }

        foreach (var line in lines)
        {
            var valid = LabResultLine.Validate(line);
            if (!valid.IsSuccess)
                return valid;
        }

        var keys = lines.Select(l => TurkishTextKey.Normalize(l.ParameterName)).ToList();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Count)
            return Result.Failure("LabResults.Validation", "Aynı parametre bir sonuçta birden fazla kez yazılamaz.");

        return ValidateResult(resultDateUtc, resultText, interpretation, lines.Count);
    }

    private static Result ValidateResult(DateTime? resultDateUtc, string? resultText, string? interpretation, int lineCount)
    {
        var hasResult = HasResult(resultText, lineCount);
        if (hasResult != resultDateUtc.HasValue)
            return Result.Failure("LabResults.Validation", "Sonuç (metin ya da değer satırı) ve sonuç tarihi birlikte verilmelidir.");
        if (!hasResult && !string.IsNullOrWhiteSpace(interpretation))
            return Result.Failure("LabResults.Validation", "Sonuç girilmeden yorum yazılamaz.");

        return Result.Success();
    }

    private static bool HasResult(string? resultText, int lineCount)
        => !string.IsNullOrWhiteSpace(resultText) || lineCount > 0;

    /// <summary>Satırları verilen liste yapar (sıra dizideki sıradır); doğrulama önceden yapılmış olmalıdır.</summary>
    private void SetLines(IReadOnlyList<LabResultLineDraft> lines)
    {
        _lines.Clear();
        for (var i = 0; i < lines.Count; i++)
            _lines.Add(new LabResultLine(Id, i + 1, lines[i]));
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
