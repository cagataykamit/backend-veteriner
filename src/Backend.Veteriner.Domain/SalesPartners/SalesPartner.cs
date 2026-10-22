using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.SalesPartners;

/// <summary>
/// Satış ortağı (ADR-037): kendi indirim kodlarını müşteriye verir, kod kullanan kiracının ilk 12 ay tahsilatından komisyon kazanır.
/// Kişisel veri asgari (ad, isteğe bağlı e-posta/telefon). <see cref="LinkedTenantId"/> ve <see cref="NormalizedEmail"/>
/// ortağın kendi kliniğinde kendi kodunu kullanmasını engellemek içindir.
/// </summary>
public sealed class SalesPartner : AggregateRoot
{
    public const int MaxNameLength = 200;
    public const int MaxEmailLength = 200;
    public const int MaxPhoneLength = 30;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = default!;
    public string? Email { get; private set; }

    /// <summary>Trim + küçük harf; kendi kodunu kullanma kontrolünde kayıt olan e-postayla karşılaştırılır.</summary>
    public string? NormalizedEmail { get; private set; }

    public string? Phone { get; private set; }

    /// <summary>Ortağın kendi kiracısı (kliniği); bu kiracı ortağın kodlarını kullanamaz.</summary>
    public Guid? LinkedTenantId { get; private set; }

    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private SalesPartner() { }

    public static SalesPartner Create(string name, string? email, string? phone, Guid? linkedTenantId, DateTime utcNow)
    {
        var partner = new SalesPartner { IsActive = true, CreatedAtUtc = DiscountTime.ToUtc(utcNow) };
        partner.Apply(name, email, phone, linkedTenantId);
        return partner;
    }

    public void Update(string name, string? email, string? phone, Guid? linkedTenantId, bool isActive, DateTime utcNow)
    {
        Apply(name, email, phone, linkedTenantId);
        IsActive = isActive;
        UpdatedAtUtc = DiscountTime.ToUtc(utcNow);
    }

    /// <summary>Kodu kullanan kiracı ya da kullanıcı ortağın kendisi mi (kendi kodunu kullanma yasağı).</summary>
    public bool IsSelf(Guid tenantId, string? redeemerEmail)
    {
        if (LinkedTenantId is { } own && own == tenantId)
            return true;

        var email = NormalizeEmail(redeemerEmail);
        return email is not null && NormalizedEmail is not null && string.Equals(email, NormalizedEmail, StringComparison.Ordinal);
    }

    public static string? NormalizeEmail(string? email)
        => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    private void Apply(string name, string? email, string? phone, Guid? linkedTenantId)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length == 0 || trimmedName.Length > MaxNameLength)
            throw new ArgumentException($"Ad 1-{MaxNameLength} karakter olmalı.", nameof(name));

        var trimmedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (trimmedEmail is { Length: > MaxEmailLength })
            throw new ArgumentException($"E-posta en fazla {MaxEmailLength} karakter olabilir.", nameof(email));

        var trimmedPhone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        if (trimmedPhone is { Length: > MaxPhoneLength })
            throw new ArgumentException($"Telefon en fazla {MaxPhoneLength} karakter olabilir.", nameof(phone));

        Name = trimmedName;
        Email = trimmedEmail;
        NormalizedEmail = NormalizeEmail(trimmedEmail);
        Phone = trimmedPhone;
        LinkedTenantId = linkedTenantId == Guid.Empty ? null : linkedTenantId;
    }
}
