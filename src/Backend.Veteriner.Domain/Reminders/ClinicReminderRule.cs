using Backend.Veteriner.Domain.Notifications;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Reminders;

/// <summary>Otomatik hatırlatıcı kuralı (TIER-003). Sayısal değerler stabildir.</summary>
public enum ClinicReminderRuleType
{
    /// <summary>Tedavideki kontrol tarihinden N gün önce.</summary>
    FollowUp = 0,

    /// <summary>Ödenmemiş en eski ücretin üzerinden N gün geçince.</summary>
    Debt = 1,

    /// <summary>Son gelişin üzerinden N gün geçti, planlı randevu yok. Ticari ileti; onay ve kliniğin ticari ileti kapısını ister.</summary>
    NoReturn = 2,
}

/// <summary>Kuralın N sınırları ve bildirim türü — tek yer. Yeni kural buraya ve <see cref="ClinicReminderRuleType"/>'a eklenir.</summary>
public sealed record ClinicReminderRuleDefinition(
    ClinicReminderRuleType Type,
    int DefaultDays,
    int MinDays,
    int MaxDays,
    OwnerNotificationType NotificationType)
{
    public static IReadOnlyList<ClinicReminderRuleDefinition> All { get; } =
    [
        new(ClinicReminderRuleType.FollowUp, 3, 1, 30, OwnerNotificationType.FollowUpReminder),
        new(ClinicReminderRuleType.Debt, 30, 1, 365, OwnerNotificationType.DebtReminder),
        new(ClinicReminderRuleType.NoReturn, 180, 30, 730, OwnerNotificationType.NoReturnReminder),
    ];

    public static ClinicReminderRuleDefinition Of(ClinicReminderRuleType type) => All.First(d => d.Type == type);

    /// <summary>Ticari ileti mi; öyleyse gönderim kliniğin ticari ileti kapısına ve sahibin onayına bağlıdır (ADR-046).</summary>
    public bool IsMarketing => OwnerNotificationChannelPolicy.CategoryOf(NotificationType) == OwnerNotificationCategory.Marketing;
}

/// <summary>
/// Kliniğin otomatik hatırlatıcı kuralı (TIER-003; docs/AUTOMATED_REMINDERS_API_CONTRACT.md). Kayıt yoksa kapalı.
/// <see cref="EnabledAtUtc"/> kapalıdan açığa geçişte yazılır: kural yalnız bu andan sonra vadesi gelenlere uygulanır, açınca birikmiş
/// geçmiş kayıtlara toplu e-posta gitmez.
/// </summary>
public sealed class ClinicReminderRule : ITenantOwned
{
    public const string DaysOutOfRangeCode = "Reminders.Rules.Validation.Days";

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public ClinicReminderRuleType RuleType { get; private set; }
    public bool IsEnabled { get; private set; }
    public int Days { get; private set; }
    public DateTime? EnabledAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }

    private ClinicReminderRule() { }

    public static ClinicReminderRule CreateDisabled(Guid tenantId, Guid clinicId, ClinicReminderRuleType ruleType, DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));

        return new ClinicReminderRule
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            RuleType = ruleType,
            Days = ClinicReminderRuleDefinition.Of(ruleType).DefaultDays,
            CreatedAtUtc = utcNow,
        };
    }

    public Result Update(bool isEnabled, int days, Guid userId, DateTime utcNow)
    {
        var definition = ClinicReminderRuleDefinition.Of(RuleType);
        if (days < definition.MinDays || days > definition.MaxDays)
        {
            return Result.Failure(
                DaysOutOfRangeCode,
                $"Gün değeri {definition.MinDays}-{definition.MaxDays} aralığında olmalıdır.");
        }

        if (isEnabled && !IsEnabled)
            EnabledAtUtc = utcNow;

        IsEnabled = isEnabled;
        Days = days;
        UpdatedAtUtc = utcNow;
        UpdatedByUserId = userId;
        return Result.Success();
    }
}
