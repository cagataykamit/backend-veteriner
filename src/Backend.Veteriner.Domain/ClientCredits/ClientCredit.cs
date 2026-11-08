using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.ClientCredits;

/// <summary>Kredinin kaynağı (FIN-005 K1). Sayısal değerler stabildir.</summary>
public enum ClientCreditSource
{
    /// <summary>Ödeme, tahsis edilen ücretlerin açık toplamını aştı; artan kısım kredi.</summary>
    Overpayment = 0,

    /// <summary>Ödeme hiçbir ücrete tahsis edilmedi (avans/depozito); tamamı kredi.</summary>
    Prepayment = 1,
}

/// <summary>Kredi durumu; saklanmaz, alanlardan hesaplanır.</summary>
public enum ClientCreditStatus
{
    Open = 0,
    Used = 1,
    Refunded = 2,
    Voided = 3,
}

/// <summary>
/// Müşteri kredisi (FIN-005): alınmış bir ödemenin ücretlere bağlanmayan kısmı. Kredi her zaman bir ödemeden doğar
/// (<see cref="Payment.Settle"/>), müşteri + klinik kapsamlıdır, yalnız TRY. Sonraki ücretlere mahsup edilir
/// (<see cref="Apply"/>); mahsup ücretin tahsis sayacını ödeme tahsisiyle aynı yoldan artırır, böylece açık tutar ve bakiye
/// ek kural gerektirmez. Kalan tutar iade edilebilir (kasa çıkışı); kaynak ödeme iptal edilirse kullanılmamış kredi de kapanır.
/// </summary>
public sealed class ClientCredit : AggregateRoot, IClinicScoped, ITenantOwned
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid SourcePaymentId { get; private set; }
    public ClientCreditSource Source { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>Aktif mahsupların toplamı; <c>0 ≤ UsedAmount ≤ Amount</c>. Yalnız <see cref="Apply"/> ve <see cref="ReverseApplication"/> değiştirir.</summary>
    public decimal UsedAmount { get; private set; }

    public string Currency { get; private set; } = Charge.DefaultCurrency;
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? RefundedAtUtc { get; private set; }
    public Guid? RefundedByUserId { get; private set; }
    public decimal? RefundedAmount { get; private set; }
    public PaymentMethod? RefundMethod { get; private set; }

    /// <summary>Nakit iadede nakdin çıktığı kasa (TIER-008); nakitte boşsa kliniğin Ana kasasıdır.</summary>
    public Guid? RefundCashRegisterId { get; private set; }

    public string? RefundReason { get; private set; }

    /// <summary>Kaynak ödeme iptal edildi (kullanılmamış kredi ödemeyle birlikte kapanır).</summary>
    public DateTime? VoidedAtUtc { get; private set; }

    /// <summary>Başarılı mutasyon sayacı; optimistic concurrency token.</summary>
    public long MutationSequence { get; private set; }

    public bool IsRefunded => RefundedAtUtc.HasValue;
    public bool IsVoided => VoidedAtUtc.HasValue;
    public bool IsClosed => IsRefunded || IsVoided;

    /// <summary>Mahsup edilebilir kalan; kapalı kredide 0.</summary>
    public decimal RemainingAmount => IsClosed ? 0m : Amount - UsedAmount;

    public ClientCreditStatus Status => IsVoided
        ? ClientCreditStatus.Voided
        : IsRefunded
            ? ClientCreditStatus.Refunded
            : UsedAmount >= Amount ? ClientCreditStatus.Used : ClientCreditStatus.Open;

    private ClientCredit() { }

    internal static ClientCredit FromPayment(Payment payment, decimal amount, ClientCreditSource source, Guid createdByUserId, DateTime nowUtc)
        => new()
        {
            TenantId = payment.TenantId,
            ClinicId = payment.ClinicId,
            ClientId = payment.ClientId,
            SourcePaymentId = payment.Id,
            Source = source,
            Amount = amount,
            Currency = payment.Currency,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
        };

    /// <summary>
    /// Krediden ücrete mahsup: ücretin açık tutarını ve kredinin kalanını aşamaz; ücret aynı kiracı/klinik/müşteri/para biriminde
    /// olmalı. Başarısızsa hiçbir şey değişmez. Dönen satır kalıcılaştırılmalıdır.
    /// </summary>
    public Result<ClientCreditApplication> Apply(Charge charge, decimal amount, Guid userId, Guid? requestId, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(charge);

        if (IsClosed)
            return Result<ClientCreditApplication>.Failure("ClientCredits.CreditClosed", "Kredi iade ya da iptal edilmiş.");

        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            return Result<ClientCreditApplication>.Failure("ClientCredits.Validation", "Mahsup tutarı 0'dan büyük ve en fazla 2 ondalıklı olmalıdır.");

        if (charge.TenantId != TenantId || charge.ClinicId != ClinicId)
            return Result<ClientCreditApplication>.Failure("ClientCredits.ChargeClinicMismatch", "Ücret bu krediyle aynı klinikte değil.");

        if (charge.ClientId != ClientId)
            return Result<ClientCreditApplication>.Failure("ClientCredits.ChargeClientMismatch", "Ücret bu krediyle aynı müşteriye ait değil.");

        if (!string.Equals(charge.Currency, Currency, StringComparison.OrdinalIgnoreCase))
            return Result<ClientCreditApplication>.Failure("ClientCredits.Validation", "Ücret ve kredi para birimi aynı olmalıdır.");

        if (amount > RemainingAmount)
        {
            return Result<ClientCreditApplication>.Failure(
                "ClientCredits.InsufficientBalance",
                $"Kredinin kalanı yetmiyor (kalan={RemainingAmount:0.00}).");
        }

        var applied = charge.ApplyAllocation(amount);
        if (!applied.IsSuccess)
            return Result<ClientCreditApplication>.Failure(applied.Error);

        UsedAmount += amount;
        MutationSequence++;
        return Result<ClientCreditApplication>.Success(
            ClientCreditApplication.Create(TenantId, ClinicId, Id, charge.Id, amount, requestId, userId, nowUtc));
    }

    /// <summary>Yanlış mahsubu geri alır: kredi kalanı geri gelir, ücretin sayacı düşer. Gerekçe zorunlu.</summary>
    public Result ReverseApplication(ClientCreditApplication application, Charge charge, Guid userId, string? reason, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(charge);

        if (application.CreditId != Id || application.ChargeId != charge.Id)
            return Result.Failure("ClientCredits.Validation", "Mahsup bu krediye ya da ücrete ait değil.");

        if (application.IsReversed)
            return Result.Failure("ClientCredits.ApplicationAlreadyReversed", "Mahsup zaten geri alınmış.");

        if (IsClosed)
            return Result.Failure("ClientCredits.CreditClosed", "Kredi iade ya da iptal edilmiş; mahsup geri alınamaz.");

        var reasonCheck = CorrectionReason.Validate(reason, "ClientCredits.Validation");
        if (!reasonCheck.IsSuccess)
            return reasonCheck;

        if (application.Amount > UsedAmount)
            return Result.Failure("ClientCredits.Validation", "Mahsup tutarı kredinin kullanılan tutarıyla tutarsız.");

        var released = charge.ReleaseAllocation(application.Amount);
        if (!released.IsSuccess)
            return released;

        application.Reverse(userId, reason!.Trim(), nowUtc);
        UsedAmount -= application.Amount;
        MutationSequence++;
        return Result.Success();
    }

    /// <summary>Kalan kredinin tamamını müşteriye iade eder (kasa çıkışı); kredi kapanır. Kısmi iade yoktur.</summary>
    public Result Refund(Guid userId, string? reason, PaymentMethod method, DateTime nowUtc, Guid? cashRegisterId = null)
    {
        if (userId == Guid.Empty)
            return Result.Failure("ClientCredits.Validation", "İade eden kullanıcı geçersiz.");

        if (!Enum.IsDefined(method))
            return Result.Failure("ClientCredits.Validation", "İade yöntemi geçersiz.");

        if (cashRegisterId is not null && method != PaymentMethod.Cash)
            return Result.Failure("ClientCredits.Validation", "Kasa yalnız nakit iadede seçilir.");

        var reasonCheck = CorrectionReason.Validate(reason, "ClientCredits.Validation");
        if (!reasonCheck.IsSuccess)
            return reasonCheck;

        if (RemainingAmount <= 0)
            return Result.Failure("ClientCredits.NothingToRefund", "İade edilecek kredi kalmadı.");

        RefundedAmount = RemainingAmount;
        RefundedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        RefundedByUserId = userId;
        RefundMethod = method;
        RefundCashRegisterId = cashRegisterId;
        RefundReason = reason!.Trim();
        MutationSequence++;
        return Result.Success();
    }

    /// <summary>Kaynak ödeme iptal edilebilir mi: kredi kullanılmamış ve iade edilmemiş olmalı (FIN-005 K7).</summary>
    public Result CanVoidWithPayment()
        => UsedAmount > 0 || IsRefunded
            ? Result.Failure(
                "Payments.VoidBlockedByCredit",
                IsRefunded
                    ? "Bu ödemeden doğan kredi iade edilmiş; ödeme iptal edilemez."
                    : "Bu ödemeden doğan kredi ücretlere mahsup edilmiş; önce mahsupları geri alın.")
            : Result.Success();

    internal void VoidWithPayment(DateTime nowUtc)
    {
        VoidedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        MutationSequence++;
    }
}
