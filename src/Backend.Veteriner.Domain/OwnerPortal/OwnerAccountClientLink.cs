using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.OwnerPortal;

/// <summary>
/// Portal hesabı ↔ klinik müşteri kaydı bağı (ADR-031 K3, AK-3). Yalnız davet kabulüyle kurulur; kiracı ve daveti gönderen
/// klinik bağlamını taşır (aydınlatma metni bu kliniğindir). Bir müşteri kaydında en fazla bir aktif bağ olur
/// (veritabanında filtreli benzersiz indeks). Klinik erişimi kapatınca bağ <see cref="OwnerAccountClientLinkStatus.Closed"/>
/// olur ve silinmez.
/// </summary>
public sealed class OwnerAccountClientLink : ITenantOwned
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OwnerAccountId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid ClientId { get; private set; }
    public OwnerAccountClientLinkStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Daveti gönderen personel kullanıcısı.</summary>
    public Guid CreatedByUserId { get; private set; }

    public DateTime? ClosedAtUtc { get; private set; }
    public Guid? ClosedByUserId { get; private set; }

    /// <summary>Hasta sahibinin kliniğin aydınlatma metnini ilk onayladığı an (bilgilendirme kaydı; açık rıza değildir).</summary>
    public DateTime? DisclosureAcknowledgedAtUtc { get; private set; }

    private OwnerAccountClientLink() { }

    public static OwnerAccountClientLink Create(
        Guid ownerAccountId,
        Guid tenantId,
        Guid clinicId,
        Guid clientId,
        Guid createdByUserId,
        DateTime utcNow)
    {
        if (ownerAccountId == Guid.Empty)
            throw new ArgumentException("OwnerAccountId geçersiz.", nameof(ownerAccountId));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (clientId == Guid.Empty)
            throw new ArgumentException("ClientId geçersiz.", nameof(clientId));

        return new OwnerAccountClientLink
        {
            OwnerAccountId = ownerAccountId,
            TenantId = tenantId,
            ClinicId = clinicId,
            ClientId = clientId,
            CreatedByUserId = createdByUserId,
            Status = OwnerAccountClientLinkStatus.Active,
            CreatedAtUtc = ToUtc(utcNow),
        };
    }

    public bool IsActive => Status == OwnerAccountClientLinkStatus.Active;

    public Result Close(Guid closedByUserId, DateTime utcNow)
    {
        if (!IsActive)
            return Result.Failure("OwnerPortal.LinkAlreadyClosed", "Portal erişimi zaten kapalı.");
        if (closedByUserId == Guid.Empty)
            return Result.Failure("OwnerPortal.Validation", "Kapatan kullanıcı geçersiz.");

        Status = OwnerAccountClientLinkStatus.Closed;
        ClosedAtUtc = ToUtc(utcNow);
        ClosedByUserId = closedByUserId;
        return Result.Success();
    }

    /// <summary>İlk onay zamanı yazılır; tekrar çağrı değiştirmez.</summary>
    public void AcknowledgeDisclosure(DateTime utcNow)
        => DisclosureAcknowledgedAtUtc ??= ToUtc(utcNow);

    private static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
