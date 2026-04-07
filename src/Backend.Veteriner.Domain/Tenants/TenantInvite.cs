namespace Backend.Veteriner.Domain.Tenants;

/// <summary>
/// Kiracıya kullanıcı daveti. Ham token API'de bir kez döner; depoda yalnızca hash tutulur.
/// </summary>
public sealed class TenantInvite
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public string Email { get; private set; } = default!;
    public string TokenHash { get; private set; } = default!;
    public Guid OperationClaimId { get; private set; }
    public TenantInviteStatus Status { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? AcceptedAtUtc { get; private set; }
    public Guid? AcceptedByUserId { get; private set; }

    private TenantInvite() { }

    public static TenantInvite CreatePending(
        Guid tenantId,
        Guid clinicId,
        string emailNormalized,
        string tokenHash,
        Guid operationClaimId,
        DateTime expiresAtUtc,
        DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (string.IsNullOrWhiteSpace(emailNormalized))
            throw new ArgumentException("E-posta boş olamaz.", nameof(emailNormalized));
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("Token hash boş olamaz.", nameof(tokenHash));
        if (operationClaimId == Guid.Empty)
            throw new ArgumentException("OperationClaimId geçersiz.", nameof(operationClaimId));

        var start = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime();

        return new TenantInvite
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            Email = emailNormalized.Trim().ToLowerInvariant(),
            TokenHash = tokenHash,
            OperationClaimId = operationClaimId,
            Status = TenantInviteStatus.Pending,
            ExpiresAtUtc = expiresAtUtc.Kind == DateTimeKind.Utc ? expiresAtUtc : expiresAtUtc.ToUniversalTime(),
            CreatedAtUtc = start,
            AcceptedAtUtc = null,
            AcceptedByUserId = null,
        };
    }

    public void MarkAccepted(Guid userId, DateTime utcNow)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId geçersiz.", nameof(userId));
        if (Status != TenantInviteStatus.Pending)
            throw new InvalidOperationException("Yalnızca bekleyen davet kabul edilebilir.");

        Status = TenantInviteStatus.Accepted;
        AcceptedAtUtc = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime();
        AcceptedByUserId = userId;
    }
}
