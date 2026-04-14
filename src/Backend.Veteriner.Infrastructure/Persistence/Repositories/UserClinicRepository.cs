using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories;

public sealed class UserClinicRepository : IUserClinicRepository
{
    private readonly AppDbContext _db;

    public UserClinicRepository(AppDbContext db) => _db = db;

    public Task<bool> ExistsAsync(Guid userId, Guid clinicId, CancellationToken ct)
        => _db.UserClinics.AsNoTracking()
            .AnyAsync(x => x.UserId == userId && x.ClinicId == clinicId, ct);

    public Task<bool> ExistsActiveInTenantAsync(Guid userId, Guid tenantId, Guid clinicId, CancellationToken ct)
        => (
            from uc in _db.UserClinics.AsNoTracking()
            join c in _db.Clinics.AsNoTracking() on uc.ClinicId equals c.Id
            where uc.UserId == userId
                  && uc.ClinicId == clinicId
                  && c.TenantId == tenantId
                  && c.IsActive
            select uc.ClinicId
        ).AnyAsync(ct);

    public async Task<IReadOnlyList<Clinic>> ListAccessibleClinicsAsync(
        Guid userId,
        Guid tenantId,
        bool? isActive,
        CancellationToken ct)
    {
        var q =
            from uc in _db.UserClinics.AsNoTracking()
            join c in _db.Clinics.AsNoTracking() on uc.ClinicId equals c.Id
            where uc.UserId == userId
                  && c.TenantId == tenantId
            select c;

        if (isActive.HasValue)
            q = q.Where(c => c.IsActive == isActive.Value);

        return await q
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .ToListAsync(ct);
    }
}
