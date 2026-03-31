using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Clients;

/// <summary>
/// Kiracıya bağlı müşteri (hayvan sahibi).
/// Telefon opsiyonel; doluysa <see cref="TurkishMobilePhone"/> ile 905XXXXXXXXX saklanır (Phone ve PhoneNormalized aynı).
/// </summary>
public sealed class Client : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public string FullName { get; private set; } = default!;
    /// <summary>Trim + küçük harf; boşsa null.</summary>
    public string? Email { get; private set; }
    /// <summary>Türkiye cep standardı 12 hane (905XXXXXXXXX) veya null.</summary>
    public string? Phone { get; private set; }
    /// <summary>Serbest metin adres; boş/whitespace ise null.</summary>
    public string? Address { get; private set; }
    /// <summary>Mükerrer (e-posta+telefon) için; Phone ile aynı standart.</summary>
    public string? PhoneNormalized { get; private set; }

    private Client() { }

    public Client(Guid tenantId, string fullName, string? phone = null, string? email = null, string? address = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Ad soyad boş olamaz.", nameof(fullName));

        TenantId = tenantId;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = null;
        FullName = fullName.Trim();
        Email = NormalizeEmailForStorage(email);
        ApplyPhone(phone);
        Address = NormalizeAddress(address);
    }

    public void UpdateDetails(string fullName, string? email, string? phone, string? address)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Ad soyad boş olamaz.", nameof(fullName));
        FullName = fullName.Trim();
        Email = NormalizeEmailForStorage(email);
        ApplyPhone(phone);
        Address = NormalizeAddress(address);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private void ApplyPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            Phone = null;
            PhoneNormalized = null;
            return;
        }

        if (!TurkishMobilePhone.TryNormalize(phone, out var norm) || norm is null)
            throw new ArgumentException("Geçersiz Türkiye cep telefonu.", nameof(phone));

        Phone = norm;
        PhoneNormalized = norm;
    }

    /// <summary>Mükerrer kontrol ve saklama ile aynı kural (trim + küçük harf).</summary>
    public static string? NormalizeEmailForStorage(string? email)
        => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    private static string? NormalizeAddress(string? address)
        => string.IsNullOrWhiteSpace(address) ? null : address.Trim();
}
