namespace Backend.Veteriner.Domain.OwnerPortal;

/// <summary>Portal davetinin saklanan durumu. Süresi geçmiş bekleyen davet <see cref="OwnerPortalInvite.IsExpired"/> ile anlaşılır.</summary>
public enum OwnerPortalInviteStatus
{
    Pending = 0,
    Accepted = 1,
    Revoked = 2,
}
