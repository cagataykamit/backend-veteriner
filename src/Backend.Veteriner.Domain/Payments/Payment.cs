using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Payments;

/// <summary>
/// Klinik tahsilat kaydı (sade çekirdek). Fatura / iade / kasa kapanışı bu aggregate dışındadır.
/// </summary>
public sealed class Payment : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid? PetId { get; private set; }
    public Guid? AppointmentId { get; private set; }
    public Guid? ExaminationId { get; private set; }

    /// <summary>Tutar; para birimi <see cref="Currency"/> ile birlikte yorumlanır.</summary>
    public decimal Amount { get; private set; }

    /// <summary>ISO 4217 alpha-3 (örn. TRY). Tek kiracılık TR operasyonu için varsayılan TRY.</summary>
    public string Currency { get; private set; } = default!;

    public PaymentMethod Method { get; private set; }
    public DateTime PaidAtUtc { get; private set; }
    public string? Notes { get; private set; }

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
        string? notes)
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
    }

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
