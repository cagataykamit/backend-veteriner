using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

/// <summary>
/// Kiracı üyeliği tekil okuma: <c>TenantId</c> + <c>UserId</c> eşleşmesi zorunludur.
/// <c>User</c> include edilir (email/emailConfirmed DTO alanları). Farklı kiracının üyesi bu spec ile
/// kesinlikle bulunmaz — 404 maskelemesi handler seviyesinde uygulanır.
/// </summary>
public sealed class UserTenantByMemberSpec : Specification<UserTenant>
{
    public Guid TenantIdFilter { get; }
    public Guid UserIdFilter { get; }

    public UserTenantByMemberSpec(Guid tenantId, Guid userId)
    {
        TenantIdFilter = tenantId;
        UserIdFilter = userId;
        Query.Where(ut => ut.TenantId == tenantId && ut.UserId == userId);
        Query.Include(ut => ut.User!);
    }
}
