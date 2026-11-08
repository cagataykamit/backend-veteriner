using Backend.Veteriner.Domain.CashCounts;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.CashRegisters;

public enum CashMovementKind
{
    /// <summary>Kasaya giriş (ör. bozuk para).</summary>
    In = 1,

    /// <summary>Kasadan çıkış (ör. bankaya yatırma, masraf).</summary>
    Out = 2,

    /// <summary>Aktarımın hedef kasadaki bacağı.</summary>
    TransferIn = 3,

    /// <summary>Aktarımın kaynak kasadaki bacağı.</summary>
    TransferOut = 4,
}

/// <summary>
/// Tahsilat ve iade dışındaki kasa hareketi: giriş/çıkış ya da kasalar arası aktarımın bir bacağı. Tutar sonradan değişmez; düzeltme
/// gerekçeli iptal ve yeni kayıttır. İptal edilen hareket hiç olmamış sayılır (kasa bakiyesine girmez), kaydı kalır.
/// Aktarım aynı <see cref="TransferId"/> ile iki bacaktır; ikisi birlikte yazılır ve birlikte iptal edilir.
/// </summary>
public sealed class CashMovement : ITenantOwned, IClinicScoped
{
    public const decimal MaxAmount = CashCount.MaxAmount;
    public const int AmountDecimals = CashCount.AmountDecimals;
    public const int MaxDescriptionLength = 500;
    public const string ValidationCode = "CashMovements.Validation";

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid CashRegisterId { get; private set; }
    public CashMovementKind Kind { get; private set; }

    /// <summary>Hareketin günü (İstanbul takvim günü).</summary>
    public DateOnly BusinessDate { get; private set; }

    /// <summary>Pozitif tutar; yönü <see cref="Kind"/> belirler.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = CashCount.DefaultCurrency;

    /// <summary>Gelir/gider türü; yalnız giriş/çıkışta.</summary>
    public Guid? CategoryId { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Aktarımın iki bacağını bağlar.</summary>
    public Guid? TransferId { get; private set; }

    /// <summary>Aktarımda karşı kasa (çıkışta hedef, girişte kaynak).</summary>
    public Guid? CounterpartCashRegisterId { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? VoidedAtUtc { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public string? VoidReason { get; private set; }
    public bool IsVoided => VoidedAtUtc.HasValue;

    /// <summary>Başarılı mutasyon sayacı; optimistic concurrency token. İptal 1 artırır.</summary>
    public long MutationSequence { get; private set; }

    public bool IsTransfer => TransferId.HasValue;

    /// <summary>Kasaya etkisi: giriş artı, çıkış eksi.</summary>
    public decimal SignedAmount => Kind is CashMovementKind.In or CashMovementKind.TransferIn ? Amount : -Amount;

    private CashMovement() { }

    /// <param name="today">İstanbul'a göre bugün; ileri tarihli hareket girilemez.</param>
    public static Result<CashMovement> Create(
        CashRegister register,
        CashMovementKind kind,
        DateOnly businessDate,
        DateOnly today,
        decimal amount,
        Guid? categoryId,
        string? description,
        Guid createdByUserId,
        DateTime nowUtc)
    {
        if (kind is not (CashMovementKind.In or CashMovementKind.Out))
            return Invalid("Giriş ya da çıkış seçilmelidir.");
        if (categoryId is null || categoryId == Guid.Empty)
            return Invalid("Gelir/gider türü seçilmelidir.");

        return Build(register, kind, businessDate, today, amount, categoryId, description, null, null, createdByUserId, nowUtc);
    }

    /// <summary>Aynı klinikteki iki aktif kasa arasında aktarım: kaynaktan çıkış ve hedefe giriş bacakları.</summary>
    public static Result<(CashMovement Out, CashMovement In)> CreateTransfer(
        CashRegister source,
        CashRegister target,
        DateOnly businessDate,
        DateOnly today,
        decimal amount,
        string? description,
        Guid createdByUserId,
        DateTime nowUtc)
    {
        if (source.Id == target.Id)
            return TransferInvalid("Kaynak ve hedef kasa aynı olamaz.");
        if (source.TenantId != target.TenantId || source.ClinicId != target.ClinicId)
            return TransferInvalid("Aktarım yalnız aynı klinikteki kasalar arasında yapılır.");

        var transferId = Guid.NewGuid();
        var outLeg = Build(source, CashMovementKind.TransferOut, businessDate, today, amount, null, description, transferId, target.Id, createdByUserId, nowUtc);
        if (!outLeg.IsSuccess)
            return Result<(CashMovement, CashMovement)>.Failure(outLeg.Error);

        var inLeg = Build(target, CashMovementKind.TransferIn, businessDate, today, amount, null, description, transferId, source.Id, createdByUserId, nowUtc);
        return inLeg.IsSuccess
            ? Result<(CashMovement, CashMovement)>.Success((outLeg.Value!, inLeg.Value!))
            : Result<(CashMovement, CashMovement)>.Failure(inLeg.Error);
    }

    public Result Void(Guid voidedByUserId, string? reason, DateTime nowUtc)
    {
        if (IsVoided)
            return Result.Failure("CashMovements.AlreadyVoided", "Kasa hareketi zaten iptal edilmiş.");
        if (voidedByUserId == Guid.Empty)
            return Result.Failure(ValidationCode, "İptal eden kullanıcı geçersiz.");

        var reasonCheck = CorrectionReason.Validate(reason, ValidationCode);
        if (!reasonCheck.IsSuccess)
            return reasonCheck;

        VoidedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        VoidedByUserId = voidedByUserId;
        VoidReason = reason!.Trim();
        MutationSequence++;
        return Result.Success();
    }

    private static Result<CashMovement> Build(
        CashRegister register,
        CashMovementKind kind,
        DateOnly businessDate,
        DateOnly today,
        decimal amount,
        Guid? categoryId,
        string? description,
        Guid? transferId,
        Guid? counterpartId,
        Guid createdByUserId,
        DateTime nowUtc)
    {
        var accepts = register.EnsureAcceptsMovements();
        if (!accepts.IsSuccess)
            return Result<CashMovement>.Failure(accepts.Error);
        if (createdByUserId == Guid.Empty)
            return Invalid("Kaydeden kullanıcı geçersiz.");
        if (businessDate > today)
            return Invalid("İleri tarihli kasa hareketi girilemez.");
        if (amount <= 0 || amount > MaxAmount || decimal.Round(amount, AmountDecimals) != amount)
            return Invalid($"Tutar 0'dan büyük, en çok {MaxAmount:0} ve en fazla {AmountDecimals} ondalıklı olmalıdır.");

        var text = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (text?.Length > MaxDescriptionLength)
            return Invalid($"Açıklama en fazla {MaxDescriptionLength} karakter olabilir.");

        return Result<CashMovement>.Success(new CashMovement
        {
            TenantId = register.TenantId,
            ClinicId = register.ClinicId,
            CashRegisterId = register.Id,
            Kind = kind,
            BusinessDate = businessDate,
            Amount = amount,
            CategoryId = categoryId,
            Description = text,
            TransferId = transferId,
            CounterpartCashRegisterId = counterpartId,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
        });
    }

    private static Result<CashMovement> Invalid(string message) => Result<CashMovement>.Failure(ValidationCode, message);

    private static Result<(CashMovement, CashMovement)> TransferInvalid(string message)
        => Result<(CashMovement, CashMovement)>.Failure(ValidationCode, message);
}
