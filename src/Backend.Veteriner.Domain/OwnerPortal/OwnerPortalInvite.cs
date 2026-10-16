using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.OwnerPortal;

/// <summary>
/// Klinikten hasta sahibine portal daveti (ADR-031 K3). Tek kullanımlık ve sürelidir; ham token yalnız e-postada bulunur,
/// depoda SHA-256 özeti tutulur. <see cref="Email"/> gönderim anındaki müşteri e-postasıdır: kabulde müşteri e-postası
/// değişmişse davet geçersizdir (handler denetler).
/// </summary>
public sealed class OwnerPortalInvite
{
    public const int TokenHashLength = 64;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid ClientId { get; private set; }
    public string Email { get; private set; } = default!;
    public string TokenHash { get; private set; } = default!;
    public OwnerPortalInviteStatus Status { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime? AcceptedAtUtc { get; private set; }
    public Guid? AcceptedOwnerAccountId { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }

    /// <summary>İptal eden personel; yeni davetle geçersiz kalan davette yeni daveti gönderen kullanıcıdır.</summary>
    public Guid? RevokedByUserId { get; private set; }

    private OwnerPortalInvite() { }

    public static OwnerPortalInvite CreatePending(
        Guid tenantId,
        Guid clinicId,
        Guid clientId,
        string email,
        string tokenHash,
        Guid createdByUserId,
        DateTime expiresAtUtc,
        DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (clientId == Guid.Empty)
            throw new ArgumentException("ClientId geçersiz.", nameof(clientId));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("Daveti gönderen kullanıcı geçersiz.", nameof(createdByUserId));
        var normalizedEmail = OwnerAccount.NormalizeEmail(email)
            ?? throw new ArgumentException("E-posta boş olamaz.", nameof(email));
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("Token özeti boş olamaz.", nameof(tokenHash));

        var now = ToUtc(utcNow);
        var expires = ToUtc(expiresAtUtc);
        if (expires <= now)
            throw new ArgumentException("Davet süresi gelecekte olmalıdır.", nameof(expiresAtUtc));

        return new OwnerPortalInvite
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            ClientId = clientId,
            Email = normalizedEmail,
            TokenHash = tokenHash,
            CreatedByUserId = createdByUserId,
            Status = OwnerPortalInviteStatus.Pending,
            ExpiresAtUtc = expires,
            CreatedAtUtc = now,
        };
    }

    public bool IsExpired(DateTime utcNow) => ToUtc(utcNow) >= ExpiresAtUtc;

    /// <summary>Kabul edilebilir: bekliyor ve süresi geçmemiş.</summary>
    public bool IsUsable(DateTime utcNow) => Status == OwnerPortalInviteStatus.Pending && !IsExpired(utcNow);

    /// <summary>Tek kullanımlık kabul. Geçersiz davette sebep ayırt edilmez (<c>Portal.Invites.NotFound</c>).</summary>
    public Result Accept(Guid ownerAccountId, DateTime utcNow)
    {
        if (ownerAccountId == Guid.Empty)
            throw new ArgumentException("OwnerAccountId geçersiz.", nameof(ownerAccountId));
        if (!IsUsable(utcNow))
            return Result.Failure("Portal.Invites.NotFound", "Davet geçersiz veya süresi dolmuş.");

        Status = OwnerPortalInviteStatus.Accepted;
        AcceptedAtUtc = ToUtc(utcNow);
        AcceptedOwnerAccountId = ownerAccountId;
        return Result.Success();
    }

    /// <summary>Bekleyen daveti iptal eder (süresi geçmiş olsa da).</summary>
    public Result Revoke(Guid revokedByUserId, DateTime utcNow)
    {
        if (Status != OwnerPortalInviteStatus.Pending)
            return Result.Failure("OwnerPortal.InviteNotPending", "Yalnız bekleyen davet iptal edilebilir.");

        Status = OwnerPortalInviteStatus.Revoked;
        RevokedAtUtc = ToUtc(utcNow);
        RevokedByUserId = revokedByUserId;
        return Result.Success();
    }

    private static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
