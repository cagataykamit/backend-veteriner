using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Administrations;

/// <summary>
/// Hastaya klinikte fiilen uygulanan katalog ürünü (uygulama kaydı). Reçeteden ayrıdır; stok düşümü
/// (<c>Out</c> hareketi, kaynak = bu kaydın kimliği) uygulama katmanında aynı transaction'da yazılır.
/// Düzenlenmez: yanlış kayıt gerekçeyle iptal edilir (<see cref="Void"/>).
/// </summary>
public sealed class Administration : AggregateRoot, IClinicScoped
{
    public const decimal MaxQuantity = 100_000m;
    public const int QuantityDecimals = 3;
    public const int MaxNotesLength = 2000;
    public const string LateEntryReasonRequiredCode = "Administrations.LateEntryReasonRequired";

    /// <summary>Uygulamanın düştüğü stok hareketinin <c>StockMovement.ReferenceType</c> değeri (kaynak izi).</summary>
    public const string StockReferenceType = "Administration";

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid PetId { get; private set; }
    public Guid? ExaminationId { get; private set; }
    public Guid? TreatmentId { get; private set; }
    public Guid ProductId { get; private set; }

    /// <summary>Ürünün stok birimi cinsinden miktar (birim dönüşümü yoktur).</summary>
    public decimal Quantity { get; private set; }

    public DateTime AdministeredAtUtc { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>İptal edilmiş uygulama silinmez; stok ters hareketle geri verilmiştir.</summary>
    public DateTime? VoidedAtUtc { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public string? VoidReason { get; private set; }
    public bool IsVoided => VoidedAtUtc.HasValue;

    /// <summary>İptalin stok sonucu (yalnız iptal edilmişse dolu); <see cref="AdministrationVoidStockOutcome.WrittenOff"/> zarar işaretidir.</summary>
    public AdministrationVoidStockOutcome? VoidStockOutcome { get; private set; }

    /// <summary>Bağlı muayene kayıt anında kesinleşmişse true (sonradan ekleme); ekleyen ve zaman <see cref="CreatedByUserId"/>/<see cref="CreatedAtUtc"/>.</summary>
    public bool AddedAfterFinalization { get; private set; }

    /// <summary>Yalnız <see cref="AddedAfterFinalization"/> iken dolu zorunlu gerekçe.</summary>
    public string? LateEntryReason { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>İstemci tekrar koruması anahtarı; aynı kiracıda benzersizdir, verilmeyebilir.</summary>
    public Guid? RequestId { get; private set; }

    /// <summary>Düşülen parti; <c>null</c> = partisiz (eski/devir) bakiye.</summary>
    public Guid? BatchId { get; private set; }

    /// <summary>Bu uygulamayı aşı kaydıyla birlikte yazan <c>Vaccination</c>; <c>null</c> = bağımsız uygulama. Bağlı uygulama aşı olayının parçasıdır (çift sayım yok).</summary>
    public Guid? VaccinationId { get; private set; }

    /// <summary>Eşzamanlı iptalleri ayırt eden belirteç (iptal stok iadesi yazmasa da tek kazanan olur).</summary>
    public byte[] RowVersion { get; private set; } = default!;

    private Administration() { }

    public static Result<Administration> Create(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid? examinationId,
        Guid? treatmentId,
        Guid productId,
        decimal quantity,
        DateTime administeredAtUtc,
        string? notes,
        bool addedAfterFinalization,
        string? lateEntryReason,
        Guid createdByUserId,
        DateTime createdAtUtc,
        Guid? requestId,
        Guid? vaccinationId = null)
    {
        if (tenantId == Guid.Empty)
            return Invalid("TenantId geçersiz.");
        if (clinicId == Guid.Empty)
            return Invalid("ClinicId geçersiz.");
        if (petId == Guid.Empty)
            return Invalid("PetId zorunludur.");
        if (productId == Guid.Empty)
            return Invalid("ProductId zorunludur.");
        if (examinationId == Guid.Empty || treatmentId == Guid.Empty || requestId == Guid.Empty || vaccinationId == Guid.Empty)
            return Invalid("ExaminationId, TreatmentId, RequestId ve VaccinationId verilirse geçerli bir kimlik olmalıdır.");
        if (createdByUserId == Guid.Empty)
            return Invalid("Oluşturan kullanıcı geçersiz.");

        if (quantity <= 0 || quantity > MaxQuantity || decimal.Round(quantity, QuantityDecimals) != quantity)
            return Invalid($"Miktar 0'dan büyük, en fazla {MaxQuantity:0} ve en fazla {QuantityDecimals} ondalıklı olmalıdır.");

        var text = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (text is not null && text.Length > MaxNotesLength)
            return Invalid($"Not en fazla {MaxNotesLength} karakter olabilir.");

        // Gerekçe yalnız sonradan eklemede zorunlu ve saklanır; aksi hâlde yok sayılır.
        string? lateReason = null;
        if (addedAfterFinalization)
        {
            var reasonCheck = CorrectionReason.Validate(lateEntryReason, LateEntryReasonRequiredCode);
            if (!reasonCheck.IsSuccess)
                return Result<Administration>.Failure(reasonCheck.Error);

            lateReason = lateEntryReason!.Trim();
        }

        return Result<Administration>.Success(new Administration
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            PetId = petId,
            ExaminationId = examinationId,
            TreatmentId = treatmentId,
            ProductId = productId,
            Quantity = quantity,
            AdministeredAtUtc = NormalizeUtc(administeredAtUtc),
            Notes = text,
            AddedAfterFinalization = addedAfterFinalization,
            LateEntryReason = lateReason,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = NormalizeUtc(createdAtUtc),
            RequestId = requestId,
            VaccinationId = vaccinationId,
        });
    }

    /// <summary>Stok düşümü sırasında seçilen partiyi bağlar (partisiz bakiyeden düşüldüyse çağrılmaz).</summary>
    public void LinkBatch(Guid batchId)
    {
        if (batchId == Guid.Empty)
            throw new ArgumentException("BatchId geçersiz.", nameof(batchId));

        BatchId = batchId;
    }

    /// <summary>
    /// Uygulamayı gerekçe ve stok sonucuyla iptal eder. <see cref="AdministrationVoidStockOutcome.StockReturned"/> için
    /// stok iadesi (ters hareket) çağıranın sorumluluğudur; <see cref="AdministrationVoidStockOutcome.WrittenOff"/> stoğa dokunmaz.
    /// </summary>
    public Result Void(Guid voidedByUserId, string? reason, AdministrationVoidStockOutcome stockOutcome, DateTime nowUtc)
    {
        if (IsVoided)
            return Result.Failure("Administrations.AlreadyVoided", "Uygulama zaten iptal edilmiş.");

        if (voidedByUserId == Guid.Empty)
            return Result.Failure("Administrations.Validation", "İptal eden kullanıcı geçersiz.");

        var reasonCheck = CorrectionReason.Validate(reason, "Administrations.Validation");
        if (!reasonCheck.IsSuccess)
            return reasonCheck;

        VoidedAtUtc = NormalizeUtc(nowUtc);
        VoidedByUserId = voidedByUserId;
        VoidReason = reason!.Trim();
        VoidStockOutcome = stockOutcome;
        return Result.Success();
    }

    private static Result<Administration> Invalid(string message)
        => Result<Administration>.Failure("Administrations.Validation", message);

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
