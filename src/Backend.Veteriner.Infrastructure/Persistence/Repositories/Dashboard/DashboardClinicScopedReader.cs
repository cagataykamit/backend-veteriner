using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Dashboard.Specs;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Dashboard;

/// <summary>
/// Dashboard özeti için klinik-dar Client/Pet sayım ve listeleme okuyucusu.
/// <see cref="IDashboardClinicScopedReader"/> sözleşmesinin gereği Appointment üzerinden ilişki kurulur;
/// Client/Pet entity'lerinde <c>ClinicId</c> yoktur.
/// </summary>
public sealed class DashboardClinicScopedReader : IDashboardClinicScopedReader
{
    private readonly AppDbContext _db;

    public DashboardClinicScopedReader(AppDbContext db) => _db = db;

    public Task<int> CountPetsAtClinicAsync(Guid tenantId, Guid clinicId, CancellationToken ct = default)
    {
        return _db.Appointments.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.ClinicId == clinicId)
            .Select(a => a.PetId)
            .Distinct()
            .CountAsync(ct);
    }

    public async Task<int> CountClientsAtClinicAsync(Guid tenantId, Guid clinicId, CancellationToken ct = default)
    {
        // Join: Appointment (ClinicId) -> Pet (ClientId). Distinct ClientId sayısı.
        var clientIds = _db.Appointments.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.ClinicId == clinicId)
            .Join(
                _db.Pets.AsNoTracking().Where(p => p.TenantId == tenantId),
                a => a.PetId,
                p => p.Id,
                (a, p) => p.ClientId)
            .Distinct();

        return await clientIds.CountAsync(ct);
    }

    public async Task<IReadOnlyList<DashboardRecentPetRow>> ListRecentPetsAtClinicAsync(
        Guid tenantId,
        Guid clinicId,
        int take,
        CancellationToken ct = default)
    {
        if (take <= 0)
            return Array.Empty<DashboardRecentPetRow>();

        // Adım 1: bu klinikteki her Pet için en güncel ScheduledAtUtc; DESC sıralı top N.
        var topPets = await _db.Appointments.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.ClinicId == clinicId)
            .GroupBy(a => a.PetId)
            .Select(g => new { PetId = g.Key, LastAt = g.Max(a => a.ScheduledAtUtc) })
            .OrderByDescending(x => x.LastAt)
            .ThenBy(x => x.PetId)
            .Take(take)
            .ToListAsync(ct);

        if (topPets.Count == 0)
            return Array.Empty<DashboardRecentPetRow>();

        var petIds = topPets.Select(x => x.PetId).ToArray();

        // Adım 2: Pet + Species navigasyonuyla projeksiyon.
        var pets = await _db.Pets.AsNoTracking()
            .Where(p => p.TenantId == tenantId && petIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                p.ClientId,
                p.Name,
                SpeciesName = p.Species != null ? p.Species.Name : string.Empty
            })
            .ToListAsync(ct);

        var byId = pets.ToDictionary(p => p.Id);

        // Sıra adım 1'den korunur.
        var result = new List<DashboardRecentPetRow>(topPets.Count);
        foreach (var row in topPets)
        {
            if (byId.TryGetValue(row.PetId, out var p))
                result.Add(new DashboardRecentPetRow(p.Id, p.ClientId, p.Name, p.SpeciesName));
        }

        return result;
    }

    public async Task<IReadOnlyList<DashboardRecentClientRow>> ListRecentClientsAtClinicAsync(
        Guid tenantId,
        Guid clinicId,
        int take,
        CancellationToken ct = default)
    {
        if (take <= 0)
            return Array.Empty<DashboardRecentClientRow>();

        // Appointment -> Pet join; ClientId bazında en güncel randevu; DESC sıralı top N.
        var topClients = await _db.Appointments.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.ClinicId == clinicId)
            .Join(
                _db.Pets.AsNoTracking().Where(p => p.TenantId == tenantId),
                a => a.PetId,
                p => p.Id,
                (a, p) => new { p.ClientId, a.ScheduledAtUtc })
            .GroupBy(x => x.ClientId)
            .Select(g => new { ClientId = g.Key, LastAt = g.Max(x => x.ScheduledAtUtc) })
            .OrderByDescending(x => x.LastAt)
            .ThenBy(x => x.ClientId)
            .Take(take)
            .ToListAsync(ct);

        if (topClients.Count == 0)
            return Array.Empty<DashboardRecentClientRow>();

        var clientIds = topClients.Select(x => x.ClientId).ToArray();

        var clients = await _db.Clients.AsNoTracking()
            .Where(c => c.TenantId == tenantId && clientIds.Contains(c.Id))
            .Select(c => new { c.Id, c.FullName, c.Phone })
            .ToListAsync(ct);

        var byId = clients.ToDictionary(c => c.Id);

        var result = new List<DashboardRecentClientRow>(topClients.Count);
        foreach (var row in topClients)
        {
            if (byId.TryGetValue(row.ClientId, out var c))
                result.Add(new DashboardRecentClientRow(c.Id, c.FullName, c.Phone));
        }

        return result;
    }
}
