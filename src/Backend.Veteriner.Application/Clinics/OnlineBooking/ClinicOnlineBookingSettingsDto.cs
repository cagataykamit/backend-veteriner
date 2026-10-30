using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.OwnerPortal;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics.OnlineBooking;

/// <summary>
/// Online randevu ayarları. Kayıt yoksa varsayılan (kapalı, kod yok) döner; kod ilk kayıtta veya yeniden üretimde oluşur.
/// <see cref="BookingUrl"/> portal alan adında <c>/randevu/{kod}</c>.
/// </summary>
public sealed record ClinicOnlineBookingSettingsDto(
    Guid ClinicId,
    bool IsEnabled,
    int MinLeadMinutes,
    int MaxAdvanceDays,
    string? BookingCode,
    string? BookingUrl,
    DateTime? BookingCodeGeneratedAtUtc);

public static class ClinicOnlineBookingMapping
{
    public static ClinicOnlineBookingSettingsDto ToDto(
        Guid clinicId,
        ClinicOnlineBookingSettings? row,
        OwnerPortalOptions portal,
        AppOptions app)
        => row is null
            ? new ClinicOnlineBookingSettingsDto(
                clinicId,
                false,
                ClinicOnlineBookingSettings.DefaultMinLeadMinutes,
                ClinicOnlineBookingSettings.DefaultMaxAdvanceDays,
                null,
                null,
                null)
            : new ClinicOnlineBookingSettingsDto(
                clinicId,
                row.IsEnabled,
                row.MinLeadMinutes,
                row.MaxAdvanceDays,
                row.BookingCode,
                BookingLink(portal, app, row.BookingCode),
                row.BookingCodeGeneratedAtUtc);

    /// <summary>Kliniğe özel bağlantı (ADR-042 K1): portal kökünde <c>/randevu/{kod}</c>.</summary>
    public static string BookingLink(OwnerPortalOptions portal, AppOptions app, string bookingCode)
        => $"{OwnerPortalEmails.PortalRoot(portal, app)}/randevu/{Uri.EscapeDataString(bookingCode)}";
}

public sealed class ClinicOnlineBookingSettingsByClinicSpec : Specification<ClinicOnlineBookingSettings>
{
    public ClinicOnlineBookingSettingsByClinicSpec(Guid tenantId, Guid clinicId)
        => Query.Where(x => x.TenantId == tenantId && x.ClinicId == clinicId);
}

/// <summary>Kiracılar arası kod araması; çağıran <c>ITenantScope.AsSystem</c> kapsamında çalıştırır.</summary>
public sealed class ClinicOnlineBookingSettingsByCodeSpec : Specification<ClinicOnlineBookingSettings>
{
    public ClinicOnlineBookingSettingsByCodeSpec(string bookingCode)
        => Query.Where(x => x.BookingCode == bookingCode);
}
