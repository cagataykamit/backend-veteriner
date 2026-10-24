using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Payments;

/// <summary>
/// Klinik tahsilat kaydı (sade çekirdek). Fatura / iade / kasa kapanışı bu aggregate dışındadır.
/// Yeni tahsilat bir veya daha fazla ücrete tam olarak tahsis edilir (<see cref="AllocateFully"/>);
/// eski kayıtlar <see cref="AllocatedAmount"/> = 0 ile "bağlanmamış" kalır.
/// </summary>
public sealed class Payment : AggregateRoot, IClinicScoped
{
    /// <summary>Tek tahsilat tutarı üst sınırı (anlamsız değer ve SQL taşması koruması).</summary>
    public const decimal MaxAmount = 1_000_000_000m;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid? PetId { get; private set; }
    public Guid? AppointmentId { get; private set; }
    public Guid? ExaminationId { get; private set; }

    /// <summary>Tutar; para birimi <see cref="Currency"/> ile birlikte yorumlanır.</summary>
    public decimal Amount { get; private set; }

    /// <summary>ISO 4217 alpha-3 (örn. TRY). Yeni tahsilatlar yalnızca TRY (<see cref="IsSupportedCurrency"/>).</summary>
    public string Currency { get; private set; } = default!;

    public PaymentMethod Method { get; private set; }
    public DateTime PaidAtUtc { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>Ücretlere tahsis edilen toplam; <c>0 ≤ AllocatedAmount ≤ Amount</c>. Yalnızca <see cref="ApplyAllocation"/> değiştirir.</summary>
    public decimal AllocatedAmount { get; private set; }

    public decimal UnallocatedAmount => Amount - AllocatedAmount;

    /// <summary>İstemci tekrar koruması anahtarı (eski kayıtlarda null); kiracıda benzersizdir.</summary>
    public Guid? RequestId { get; private set; }

    /// <summary>İstek içeriğinin özeti; aynı <see cref="RequestId"/> farklı içerikle gelirse çakışma ayırt edilir.</summary>
    public string? RequestHash { get; private set; }

    /// <summary>
    /// Başarılı mutasyon sayacı; optimistic concurrency token. Yeni kayıt 0 ile başlar, her mutasyon 1 artırır.
    /// </summary>
    public long MutationSequence { get; private set; }

    /// <summary>İptal edilmiş ödeme "hiç alınmamış" sayılır; kayıt silinmez, okuma yüzeylerinden düşer.</summary>
    public DateTime? VoidedAtUtc { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public string? VoidReason { get; private set; }
    public bool IsVoided => VoidedAtUtc.HasValue;

    private Payment() { }

    public Payment(
        Guid tenantId,
        Guid clinicId,
        Guid clientId,
        Guid? petId,
        Guid? appointmentId,
        Guid? examinationId,
        decimal amount,
        string currency,
        PaymentMethod method,
        DateTime paidAtUtc,
        string? notes,
        Guid? requestId = null,
        string? requestHash = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (clientId == Guid.Empty)
            throw new ArgumentException("ClientId geçersiz.", nameof(clientId));
        if (amount <= 0)
            throw new ArgumentException("Tutar sıfırdan büyük olmalıdır.", nameof(amount));
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Para birimi boş olamaz.", nameof(currency));

        var c = currency.Trim().ToUpperInvariant();
        if (c.Length != 3)
            throw new ArgumentException("Para birimi ISO 4217 alpha-3 (3 harf) olmalıdır.", nameof(currency));

        TenantId = tenantId;
        ClinicId = clinicId;
        ClientId = clientId;
        PetId = petId;
        AppointmentId = appointmentId;
        ExaminationId = examinationId;
        Amount = amount;
        Currency = c;
        Method = method;
        PaidAtUtc = NormalizeUtc(paidAtUtc);
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        RequestId = requestId;
        RequestHash = requestHash;
    }

    /// <summary>Düzeltme gerekçesi zorunludur (5–500 karakter, boşluklar kırpılır).</summary>
    public static Result ValidateCorrectionReason(string? reason)
        => CorrectionReason.Validate(reason, "Payments.Validation");

    /// <summary>Yeni tahsilatlar yalnızca TRY kabul eder (ADR-012; çoklu para birimi FIN-006).</summary>
    public static bool IsSupportedCurrency(string? currency)
        => string.Equals(currency?.Trim(), Charge.DefaultCurrency, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Verilen değerler tutar, müşteri, klinik veya para biriminden birini değiştiriyorsa true:
    /// bu bir düzeltmedir (yönetici yetkisi + gerekçe ister).
    /// </summary>
    public bool IsCorrection(Guid clinicId, Guid clientId, decimal amount, string currency)
        => clinicId != ClinicId
           || clientId != ClientId
           || amount != Amount
           || !string.Equals(currency?.Trim(), Currency, StringComparison.OrdinalIgnoreCase);

    public Result UpdateDetails(
        Guid clinicId,
        Guid clientId,
        Guid? petId,
        Guid? appointmentId,
        Guid? examinationId,
        decimal amount,
        string currency,
        PaymentMethod method,
        DateTime paidAtUtc,
        string? notes)
    {
        if (IsVoided)
            return Result.Failure("Payments.AlreadyVoided", "İptal edilmiş ödeme güncellenemez.");

        if (clinicId == Guid.Empty)
            return Result.Failure("Payments.Validation", "ClinicId gecersiz.");
        if (clientId == Guid.Empty)
            return Result.Failure("Payments.Validation", "ClientId gecersiz.");
        if (amount <= 0)
            return Result.Failure("Payments.Validation", "Tutar sifirdan buyuk olmalidir.");
        if (string.IsNullOrWhiteSpace(currency))
            return Result.Failure("Payments.Validation", "Para birimi bos olamaz.");

        var c = currency.Trim().ToUpperInvariant();
        if (c.Length != 3)
            return Result.Failure("Payments.Validation", "Para birimi ISO 4217 alpha-3 (3 harf) olmalidir.");

        if (AllocatedAmount > 0 && IsCorrection(clinicId, clientId, amount, c))
        {
            return Result.Failure(
                "Payments.UpdateBlockedByAllocation",
                "Ücrete tahsis edilmiş ödemenin tutarı, müşterisi, kliniği veya para birimi değiştirilemez.");
        }

        ClinicId = clinicId;
        ClientId = clientId;
        PetId = petId;
        AppointmentId = appointmentId;
        ExaminationId = examinationId;
        Amount = amount;
        Currency = c;
        Method = method;
        PaidAtUtc = NormalizeUtc(paidAtUtc);
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        MutationSequence++;
        return Result.Success();
    }

    /// <summary>
    /// Yeni tahsilatı verilen ücretlere tam olarak tahsis eder: toplam tutara eşit olmalı, her tahsis ücretin açık
    /// tutarını aşamaz, ücretler aynı kiracı/klinik/müşteri/para biriminde olmalı. Başarısızsa hiçbir şey değişmez.
    /// Dönen satırlar kalıcılaştırılmalıdır.
    /// </summary>
    public Result<IReadOnlyList<PaymentAllocation>> AllocateFully(
        IReadOnlyList<(Charge Charge, decimal Amount)> lines,
        Guid createdByUserId,
        DateTime nowUtc)
    {
        if (lines.Count == 0)
            return Fail("Payments.AllocationsRequired", "Ödeme en az bir ücrete tahsis edilmelidir.");

        if (AllocatedAmount != 0)
            return Fail("Payments.Validation", "Ödeme zaten tahsis edilmiş.");

        if (lines.Select(l => l.Charge.Id).Distinct().Count() != lines.Count)
            return Fail("Payments.Validation", "Aynı ücret birden fazla kez tahsis edilemez.");

        foreach (var (charge, amount) in lines)
        {
            if (amount <= 0 || decimal.Round(amount, 2) != amount)
                return Fail("Payments.Validation", "Tahsis tutarı 0'dan büyük ve en fazla 2 ondalıklı olmalıdır.");

            if (charge.TenantId != TenantId || charge.ClinicId != ClinicId)
                return Fail("PaymentAllocations.ChargeClinicMismatch", "Ücret bu ödemeyle aynı klinikte değil.");

            if (charge.ClientId != ClientId)
                return Fail("PaymentAllocations.ChargeClientMismatch", "Ücret bu ödemeyle aynı müşteriye ait değil.");

            if (!string.Equals(charge.Currency, Currency, StringComparison.OrdinalIgnoreCase))
                return Fail("Payments.Validation", "Ücret ve ödeme para birimi aynı olmalıdır.");
        }

        if (lines.Sum(l => l.Amount) != Amount)
        {
            return Fail(
                "Payments.AllocationSumMismatch",
                "Tahsis tutarları toplamı ödeme tutarına eşit olmalıdır; ödeme açık tutar kadar kaydedilir.");
        }

        foreach (var (charge, amount) in lines)
        {
            if (amount > charge.OutstandingAmount)
            {
                return Fail(
                    "PaymentAllocations.ExceedsOutstanding",
                    $"Tahsis tutarı ücretin açık tutarını aşıyor (ChargeId={charge.Id}, açık tutar={charge.OutstandingAmount:0.00}).");
            }
        }

        var allocations = new List<PaymentAllocation>(lines.Count);
        foreach (var (charge, amount) in lines)
        {
            var applied = charge.ApplyAllocation(amount);
            if (!applied.IsSuccess)
                return Result<IReadOnlyList<PaymentAllocation>>.Failure(applied.Error);

            allocations.Add(PaymentAllocation.Create(TenantId, ClinicId, Id, charge.Id, amount, createdByUserId, nowUtc));
        }

        var self = ApplyAllocation(lines.Sum(l => l.Amount));
        return self.IsSuccess
            ? Result<IReadOnlyList<PaymentAllocation>>.Success(allocations)
            : Result<IReadOnlyList<PaymentAllocation>>.Failure(self.Error);
    }

    /// <summary>
    /// Ödemeyi iptal eder ("hiç alınmamış" sayılır): her aktif tahsis geri alınır, ücret sayaçları düşer, ödeme sayacı 0 olur.
    /// <paramref name="activeAllocations"/> ödemenin tüm aktif tahsislerini ve karşılık gelen ücretleri içermelidir.
    /// Başarısızsa hiçbir şey değişmez.
    /// </summary>
    public Result Void(
        IReadOnlyList<(PaymentAllocation Allocation, Charge Charge)> activeAllocations,
        Guid voidedByUserId,
        string? reason,
        DateTime nowUtc)
    {
        if (IsVoided)
            return Result.Failure("Payments.AlreadyVoided", "Ödeme zaten iptal edilmiş.");

        if (voidedByUserId == Guid.Empty)
            return Result.Failure("Payments.Validation", "İptal eden kullanıcı geçersiz.");

        var reasonCheck = ValidateCorrectionReason(reason);
        if (!reasonCheck.IsSuccess)
            return reasonCheck;

        if (activeAllocations.Sum(x => x.Allocation.Amount) != AllocatedAmount
            || activeAllocations.Any(x => x.Allocation.PaymentId != Id
                                          || x.Allocation.ChargeId != x.Charge.Id
                                          || x.Allocation.IsReversed))
        {
            return Result.Failure("Payments.Validation", "Ödemenin tahsis kayıtları sayaçla tutarsız; iptal yapılamadı.");
        }

        foreach (var (allocation, charge) in activeAllocations)
        {
            var released = charge.ReleaseAllocation(allocation.Amount);
            if (!released.IsSuccess)
                return released;

            allocation.Reverse(nowUtc);
        }

        AllocatedAmount = 0;
        VoidedAtUtc = NormalizeUtc(nowUtc);
        VoidedByUserId = voidedByUserId;
        VoidReason = reason!.Trim();
        MutationSequence++;
        return Result.Success();
    }

    /// <summary>Tahsis sayacını artırır; <c>AllocatedAmount ≤ Amount</c> değişmezini korur.</summary>
    public Result ApplyAllocation(decimal amount)
    {
        if (IsVoided)
            return Result.Failure("Payments.AlreadyVoided", "İptal edilmiş ödemeye tahsis yapılamaz.");

        if (amount <= 0)
            return Result.Failure("Payments.Validation", "Tahsis tutarı 0'dan büyük olmalıdır.");

        if (amount > UnallocatedAmount)
        {
            return Result.Failure(
                "PaymentAllocations.ExceedsOutstanding",
                "Tahsis tutarı ödemenin bağlanmamış tutarını aşıyor.");
        }

        AllocatedAmount += amount;
        MutationSequence++;
        return Result.Success();
    }

    private static Result<IReadOnlyList<PaymentAllocation>> Fail(string code, string message)
        => Result<IReadOnlyList<PaymentAllocation>>.Failure(code, message);

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
