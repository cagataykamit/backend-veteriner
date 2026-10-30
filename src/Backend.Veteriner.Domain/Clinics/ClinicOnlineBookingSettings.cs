using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Clinics;

/// <summary>
/// Kliniğin online randevu talebi ayarları (ADR-042 K3/K6): açık/kapalı (varsayılan kapalı), en erken/en geç talep sınırı ve
/// kliniğe özel bağlantı kodu (APPT-002). Kod rastgele ve tahmin edilemez; yeniden üretilince eski bağlantı kapanır.
/// Kiracı + klinik benzersizdir; kod tüm kiracılarda benzersizdir.
/// </summary>
public sealed class ClinicOnlineBookingSettings : ITenantOwned
{
    public const int BookingCodeLength = 12;
    public const int DefaultMinLeadMinutes = 120;
    public const int DefaultMaxAdvanceDays = 30;
    public const int MaxLeadMinutes = 7 * 24 * 60;
    public const int MaxAdvanceDaysLimit = 180;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public bool IsEnabled { get; private set; }

    /// <summary>Talep edilen saat şimdiden en az bu kadar dakika sonra olmalı.</summary>
    public int MinLeadMinutes { get; private set; }

    /// <summary>Talep edilen saat şimdiden en fazla bu kadar gün sonra olabilir.</summary>
    public int MaxAdvanceDays { get; private set; }

    public string BookingCode { get; private set; } = default!;
    public DateTime BookingCodeGeneratedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private ClinicOnlineBookingSettings() { }

    /// <summary>Yeni ayar satırı; online randevu kapalı başlar.</summary>
    public static ClinicOnlineBookingSettings Create(Guid tenantId, Guid clinicId, string bookingCode, DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));

        return new ClinicOnlineBookingSettings
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            IsEnabled = false,
            MinLeadMinutes = DefaultMinLeadMinutes,
            MaxAdvanceDays = DefaultMaxAdvanceDays,
            BookingCode = ValidateCode(bookingCode),
            BookingCodeGeneratedAtUtc = utcNow,
            CreatedAtUtc = utcNow,
        };
    }

    public Result Update(bool isEnabled, int minLeadMinutes, int maxAdvanceDays, DateTime utcNow)
    {
        var validation = ValidateWindow(minLeadMinutes, maxAdvanceDays);
        if (!validation.IsSuccess)
            return validation;

        IsEnabled = isEnabled;
        MinLeadMinutes = minLeadMinutes;
        MaxAdvanceDays = maxAdvanceDays;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>Yeni bağlantı kodu; eski kod artık hiçbir kliniği çözmez.</summary>
    public void RegenerateCode(string bookingCode, DateTime utcNow)
    {
        BookingCode = ValidateCode(bookingCode);
        BookingCodeGeneratedAtUtc = utcNow;
        UpdatedAtUtc = utcNow;
    }

    /// <summary>Talep edilebilecek UTC aralığı: [şimdi + en erken, şimdi + en geç].</summary>
    public (DateTime EarliestUtc, DateTime LatestUtc) RequestWindow(DateTime utcNow)
        => (utcNow.AddMinutes(MinLeadMinutes), utcNow.AddDays(MaxAdvanceDays));

    public static Result ValidateWindow(int minLeadMinutes, int maxAdvanceDays)
    {
        if (minLeadMinutes < 0 || minLeadMinutes > MaxLeadMinutes)
            return Result.Failure("OnlineBooking.Validation", $"En erken talep süresi 0-{MaxLeadMinutes} dakika arasında olmalıdır.");
        if (maxAdvanceDays < 1 || maxAdvanceDays > MaxAdvanceDaysLimit)
            return Result.Failure("OnlineBooking.Validation", $"En geç talep süresi 1-{MaxAdvanceDaysLimit} gün arasında olmalıdır.");
        if (minLeadMinutes >= maxAdvanceDays * 24 * 60)
            return Result.Failure("OnlineBooking.Validation", "En erken talep süresi en geç talep süresinden kısa olmalıdır.");
        return Result.Success();
    }

    private static string ValidateCode(string bookingCode)
    {
        if (string.IsNullOrWhiteSpace(bookingCode) || bookingCode.Length != BookingCodeLength)
            throw new ArgumentException($"Bağlantı kodu {BookingCodeLength} karakter olmalıdır.", nameof(bookingCode));
        return bookingCode;
    }
}
