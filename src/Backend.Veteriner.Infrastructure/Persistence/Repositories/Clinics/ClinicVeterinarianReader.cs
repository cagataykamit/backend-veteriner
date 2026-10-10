using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Users.Common;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Clinics;

public sealed class ClinicVeterinarianReader : IClinicVeterinarianReader
{
    private readonly AppDbContext _db;

    public ClinicVeterinarianReader(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ClinicVeterinarianDto>> ListAsync(Guid tenantId, Guid clinicId, CancellationToken ct)
    {
        var rows = await ClinicVeterinarians(tenantId, clinicId)
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToListAsync(ct);

        return rows
            .Select(u => new ClinicVeterinarianDto(u.Id, UserDisplayName.Resolve(u.DisplayName, u.Email)))
            .OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(v => v.UserId)
            .ToList();
    }

    public Task<bool> IsClinicVeterinarianAsync(Guid userId, Guid tenantId, Guid clinicId, CancellationToken ct)
        => ClinicVeterinarians(tenantId, clinicId).AnyAsync(u => u.Id == userId, ct);

    public async Task<IReadOnlyDictionary<Guid, string?>> GetNamesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
            return new Dictionary<Guid, string?>();

        var rows = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToListAsync(ct);

        return rows.ToDictionary(u => u.Id, u => UserDisplayName.Resolve(u.DisplayName, u.Email));
    }

    private IQueryable<Backend.Veteriner.Domain.Users.User> ClinicVeterinarians(Guid tenantId, Guid clinicId)
        => from uc in _db.UserClinics.AsNoTracking()
           join c in _db.Clinics.AsNoTracking() on uc.ClinicId equals c.Id
           join u in _db.Users.AsNoTracking() on uc.UserId equals u.Id
           where uc.ClinicId == clinicId
                 && c.TenantId == tenantId
                 && c.IsActive
                 && _db.UserOperationClaims.Any(x =>
                     x.UserId == u.Id && x.OperationClaim!.Name == ClinicVeterinarianRules.ClaimName)
           select u;
}
