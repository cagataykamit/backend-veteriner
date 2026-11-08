namespace Backend.Veteriner.Domain.Notifications;

/// <summary>Hasta sahibine giden bildirim türü (PORTAL-010; ADR-031 K7). Sayısal değerler stabildir.</summary>
public enum OwnerNotificationType
{
    PortalInvite = 0,
    SignInCode = 1,
    PasswordResetCode = 2,
    AccountDeletionCode = 3,
    AccountDeleted = 4,
    AppointmentRequestReceived = 5,
    AppointmentRequestApproved = 6,
    AppointmentRequestRescheduled = 7,
    AppointmentRequestRejected = 8,
    DataRequestAnswered = 9,
    AppointmentReminder = 10,
    VaccinationReminder = 11,
    Marketing = 12,

    /// <summary>Tedavideki kontrol tarihi yaklaştı (TIER-003).</summary>
    FollowUpReminder = 13,

    /// <summary>Ödenmemiş bakiye; tutar yazılmaz (TIER-003).</summary>
    DebtReminder = 14,

    /// <summary>Son gelişin üzerinden süre geçti (TIER-003); ticari ileti sayılır.</summary>
    NoReturnReminder = 15,
}

/// <summary>Bildirim kategorisi: kanal kuralını belirler.</summary>
public enum OwnerNotificationCategory
{
    /// <summary>İşlemsel ileti: hesap, kod, talep yanıtı. Tercihle kapanmaz.</summary>
    Transactional = 0,

    /// <summary>Aşı/randevu hatırlatması: müşteri kaydının hatırlatma tercihi (<c>Client.AcceptsReminders</c>).</summary>
    Reminder = 1,

    /// <summary>Ticari elektronik ileti (ETK, İYS): bağdaki onay + kliniğin ticari ileti kapısı (ADR-046).</summary>
    Marketing = 2,
}

public enum OwnerNotificationChannel
{
    Email = 0,
    Push = 1,

    /// <summary>Personelin açtığı wa.me bağlantısı; sistem göndermez (AK-6).</summary>
    WhatsAppLink = 2,
}

/// <summary>Bir kanalın bir tür için çalışma biçimi.</summary>
public enum OwnerNotificationChannelMode
{
    /// <summary>Her zaman gönderilir.</summary>
    Always = 0,

    /// <summary>Alıcının tercihine bağlı.</summary>
    Preference = 1,

    /// <summary>Sistem göndermez; personel elle bağlantıyla iletir.</summary>
    StaffManual = 2,

    /// <summary>Kanal sağlayıcısı yapılandırılmamış (ör. push anahtarı yok).</summary>
    NotConfigured = 3,

    Disabled = 4,
}

/// <summary>
/// Bildirim türü × kanal kuralı — tek yer (PORTAL-010; ADR-031 K7). E-posta işlemsel iletide her zaman, hatırlatmada tercihe bağlı;
/// push tercih + sağlayıcı ister, gizli bilgi (kod, davet bağlantısı) push'a yazılmaz; WhatsApp yalnız personel bağlantısıdır.
/// Ticari ileti yalnız e-postadır: alıcının o klinik için onayı (bağ) ve kliniğin ticari ileti kapısı (<c>ClinicMarketingSettings</c>,
/// ADR-046) ister; kapı varsayılan kapalıdır. Hatırlatmalar kapıdan bağımsızdır.
/// </summary>
public static class OwnerNotificationChannelPolicy
{
    public static IReadOnlyList<OwnerNotificationType> AllTypes { get; } = Enum.GetValues<OwnerNotificationType>();

    public static IReadOnlyList<OwnerNotificationChannel> AllChannels { get; } = Enum.GetValues<OwnerNotificationChannel>();

    public static OwnerNotificationCategory CategoryOf(OwnerNotificationType type) => type switch
    {
        OwnerNotificationType.AppointmentReminder or OwnerNotificationType.VaccinationReminder
            or OwnerNotificationType.FollowUpReminder or OwnerNotificationType.DebtReminder => OwnerNotificationCategory.Reminder,
        OwnerNotificationType.Marketing or OwnerNotificationType.NoReturnReminder => OwnerNotificationCategory.Marketing,
        _ => OwnerNotificationCategory.Transactional,
    };

    /// <summary>Türün kanaldaki kuralı; push için sağlayıcı durumu <paramref name="pushConfigured"/> ile verilir.</summary>
    public static OwnerNotificationChannelMode ModeOf(OwnerNotificationType type, OwnerNotificationChannel channel, bool pushConfigured)
    {
        var category = CategoryOf(type);
        return channel switch
        {
            OwnerNotificationChannel.Email => category switch
            {
                OwnerNotificationCategory.Transactional => OwnerNotificationChannelMode.Always,
                OwnerNotificationCategory.Reminder => OwnerNotificationChannelMode.Preference,
                _ => OwnerNotificationChannelMode.Preference,
            },
            OwnerNotificationChannel.Push => category == OwnerNotificationCategory.Marketing || CarriesSecret(type) || IsEmailOnly(type)
                ? OwnerNotificationChannelMode.Disabled
                : pushConfigured ? OwnerNotificationChannelMode.Preference : OwnerNotificationChannelMode.NotConfigured,
            OwnerNotificationChannel.WhatsAppLink => (category == OwnerNotificationCategory.Reminder && !IsEmailOnly(type)) || type == OwnerNotificationType.PortalInvite
                ? OwnerNotificationChannelMode.StaffManual
                : OwnerNotificationChannelMode.Disabled,
            _ => OwnerNotificationChannelMode.Disabled,
        };
    }

    /// <summary>
    /// Sistemin bu kanaldan şimdi göndermesi gerekir mi. <paramref name="recipientOptedIn"/>: kanal tercihi (push için hesap
    /// tercihi, hatırlatma için müşteri kaydı, ticari için bağdaki onay; kliniğin ticari ileti kapısı çağıranda denetlenir). Personel
    /// bağlantısı hiçbir zaman sistem gönderimi değildir.
    /// </summary>
    public static bool ShouldSend(OwnerNotificationType type, OwnerNotificationChannel channel, bool pushConfigured, bool recipientOptedIn)
        => ModeOf(type, channel, pushConfigured) switch
        {
            OwnerNotificationChannelMode.Always => true,
            OwnerNotificationChannelMode.Preference => recipientOptedIn,
            _ => false,
        };

    /// <summary>Otomatik hatırlatıcılar şimdilik yalnız e-posta (TIER-003); push ve WhatsApp entegrasyonla gelir.</summary>
    private static bool IsEmailOnly(OwnerNotificationType type)
        => type is OwnerNotificationType.FollowUpReminder or OwnerNotificationType.DebtReminder or OwnerNotificationType.NoReturnReminder;

    /// <summary>Tek kullanımlık kod ve davet bağlantısı kilit ekranında görünebilecek push'a yazılmaz.</summary>
    private static bool CarriesSecret(OwnerNotificationType type)
        => type is OwnerNotificationType.SignInCode or OwnerNotificationType.PasswordResetCode
            or OwnerNotificationType.AccountDeletionCode or OwnerNotificationType.PortalInvite;
}
