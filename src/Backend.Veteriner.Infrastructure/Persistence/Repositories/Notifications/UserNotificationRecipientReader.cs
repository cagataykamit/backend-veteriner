using Backend.Veteriner.Application.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Notifications;

public sealed class UserNotificationRecipientReader : IUserNotificationRecipientReader
{
    private readonly AppDbContext _db;

    public UserNotificationRecipientReader(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Guid>> ListAsync(
        Guid tenantId,
        Guid clinicId,
        string permissionCode,
        IReadOnlyCollection<string> tenantWideClaimNames,
        CancellationToken ct)
    {
        var wide = tenantWideClaimNames.ToArray();
        return await (
                from ut in _db.UserTenants.AsNoTracking()
                where ut.TenantId == tenantId
                      && (from uoc in _db.UserOperationClaims
                          join ocp in _db.OperationClaimPermissions on uoc.OperationClaimId equals ocp.OperationClaimId
                          join p in _db.Permissions on ocp.PermissionId equals p.Id
                          where uoc.UserId == ut.UserId && p.Code == permissionCode
                          select uoc.Id).Any()
                      && (_db.UserClinics.Any(uc => uc.UserId == ut.UserId && uc.ClinicId == clinicId)
                          || _db.UserOperationClaims.Any(uoc => uoc.UserId == ut.UserId && wide.Contains(uoc.OperationClaim!.Name)))
                select ut.UserId)
            .Distinct()
            .ToListAsync(ct);
    }
}
