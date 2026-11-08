using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.CashCounts;

/// <summary>
/// Günlük kasa kontrolü (sayım; TIER-008, docs/CASH_COUNT_API_CONTRACT.md). Kasa ve İstanbul günü başına en çok bir geçerli kayıt.
/// Beklenen nakit kayıt anındaki kasanın gün neti olarak saklanır (nakit tahsilat neti, kasa giriş/çıkışı ve aktarımlar); tutarlar
/// sonradan değişmez. Düzeltme kaydı ezmez: eski kayıt gerekçeyle iptal edilir, yerine yenisi yazılır (<see cref="Correct"/>).
/// </summary>
public sealed class CashCount : ITenantOwned, IClinicScoped
{
    public const string DefaultCurrency = "TRY";
    public const decimal MaxAmount = 10_000_000m;
    public const int AmountDecimals = 2;
    public const int MaxNoteLength = 500;
    public const string ValidationCode = "CashCounts.Validation";

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }

    /// <summary>Sayılan kasa; çoklu kasa öncesi sayımlar Ana kasaya bağlanmıştır.</summary>
    public Guid CashRegisterId { get; private set; }

    /// <summary>Sayılan gün (İstanbul takvim günü).</summary>
    public DateOnly BusinessDate { get; private set; }

    /// <summary>Gün başında kasada olan nakit (personel girer).</summary>
    public decimal OpeningAmount { get; private set; }

    /// <summary>Kayıt anında kasanın gün neti: nakit tahsilat − iptal − kredi iadesi + giriş − çıkış ± aktarım.</summary>
    public decimal ExpectedCashAmount { get; private set; }

    /// <summary>Gün sonunda sayılan nakit (personel girer).</summary>
    public decimal CountedAmount { get; private set; }

    public string? Note { get; private set; }
    public string Currency { get; private set; } = DefaultCurrency;

    /// <summary>Bu kayıt bir düzeltmeyse yerine geçtiği (iptal edilen) kayıt.</summary>
    public Guid? CorrectsCashCountId { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? VoidedAtUtc { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public string? VoidReason { get; private set; }
    public bool IsVoided => VoidedAtUtc.HasValue;

    /// <summary>Başarılı mutasyon sayacı; optimistic concurrency token. İptal 1 artırır.</summary>
    public long MutationSequence { get; private set; }

    /// <summary>Kasada olması gereken: açılış + günün nakit neti.</summary>
    public decimal ExpectedClosingAmount => OpeningAmount + ExpectedCashAmount;

    /// <summary>Sayılan − olması gereken; artı kasa fazlası, eksi kasa açığı.</summary>
    public decimal DifferenceAmount => CountedAmount - ExpectedClosingAmount;

    private CashCount() { }

    /// <param name="today">İstanbul'a göre bugün; ileri tarihli sayım girilemez.</param>
    public static Result<CashCount> Create(
        Guid tenantId,
        Guid clinicId,
        Guid cashRegisterId,
        DateOnly businessDate,
        DateOnly today,
        decimal openingAmount,
        decimal expectedCashAmount,
        decimal countedAmount,
        string? note,
        Guid createdByUserId,
        DateTime nowUtc,
        Guid? correctsCashCountId = null)
    {
        if (tenantId == Guid.Empty)
            return Invalid("TenantId geçersiz.");
        if (clinicId == Guid.Empty)
            return Invalid("ClinicId geçersiz.");
        if (cashRegisterId == Guid.Empty)
            return Invalid("Kasa geçersiz.");
        if (createdByUserId == Guid.Empty)
            return Invalid("Kaydeden kullanıcı geçersiz.");
        if (businessDate > today)
            return Invalid("İleri tarihli gün için kasa sayımı girilemez.");

        if (AmountError(openingAmount, "Açılış tutarı") is { } openingError)
            return Invalid(openingError);
        if (AmountError(countedAmount, "Sayılan nakit") is { } countedError)
            return Invalid(countedError);

        var text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (text?.Length > MaxNoteLength)
            return Invalid($"Açıklama en fazla {MaxNoteLength} karakter olabilir.");

        var count = new CashCount
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            CashRegisterId = cashRegisterId,
            BusinessDate = businessDate,
            OpeningAmount = openingAmount,
            ExpectedCashAmount = expectedCashAmount,
            CountedAmount = countedAmount,
            Note = text,
            Currency = DefaultCurrency,
            CorrectsCashCountId = correctsCashCountId,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = NormalizeUtc(nowUtc),
        };

        // Fark varsa nedeni yazılır; sonradan "fark neden var" sorusu kayıttan cevaplanır.
        if (count.DifferenceAmount != 0 && text is null)
            return Invalid("Sayılan nakit beklenenden farklıysa açıklama zorunludur.");

        return Result<CashCount>.Success(count);
    }

    /// <summary>
    /// Yerine geçecek kaydı üretir ve bu kaydı gerekçeyle iptal eder; ikisi aynı işlemde kaydedilir. Gün, klinik ve kasa değişmez
    /// (yanlışsa iptal edilip doğru güne ya da kasaya yeni kayıt girilir). Yeni kayıt geçersizse bu kayıt değişmez.
    /// </summary>
    public Result<CashCount> Correct(
        decimal openingAmount,
        decimal expectedCashAmount,
        decimal countedAmount,
        string? note,
        string? reason,
        Guid userId,
        DateOnly today,
        DateTime nowUtc)
    {
        if (IsVoided)
            return Result<CashCount>.Failure("CashCounts.AlreadyVoided", "İptal edilmiş kasa sayımı düzeltilemez.");

        var reasonCheck = CorrectionReason.Validate(reason, ValidationCode);
        if (!reasonCheck.IsSuccess)
            return Result<CashCount>.Failure(reasonCheck.Error);

        var replacement = Create(
            TenantId, ClinicId, CashRegisterId, BusinessDate, today, openingAmount, expectedCashAmount, countedAmount, note, userId, nowUtc, Id);
        if (!replacement.IsSuccess)
            return replacement;

        var r = replacement.Value!;
        if (r.OpeningAmount == OpeningAmount && r.CountedAmount == CountedAmount && r.ExpectedCashAmount == ExpectedCashAmount && r.Note == Note)
            return Invalid("Düzeltme için en az bir alan değişmelidir.");

        var voided = Void(userId, reason, nowUtc);
        return voided.IsSuccess ? replacement : Result<CashCount>.Failure(voided.Error);
    }

    public Result Void(Guid voidedByUserId, string? reason, DateTime nowUtc)
    {
        if (IsVoided)
            return Result.Failure("CashCounts.AlreadyVoided", "Kasa sayımı zaten iptal edilmiş.");
        if (voidedByUserId == Guid.Empty)
            return Result.Failure(ValidationCode, "İptal eden kullanıcı geçersiz.");

        var reasonCheck = CorrectionReason.Validate(reason, ValidationCode);
        if (!reasonCheck.IsSuccess)
            return reasonCheck;

        VoidedAtUtc = NormalizeUtc(nowUtc);
        VoidedByUserId = voidedByUserId;
        VoidReason = reason!.Trim();
        MutationSequence++;
        return Result.Success();
    }

    private static string? AmountError(decimal amount, string label)
        => amount < 0 || amount > MaxAmount || decimal.Round(amount, AmountDecimals) != amount
            ? $"{label} 0 ile {MaxAmount:0} arasında ve en fazla {AmountDecimals} ondalıklı olmalıdır."
            : null;

    private static Result<CashCount> Invalid(string message) => Result<CashCount>.Failure(ValidationCode, message);

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
