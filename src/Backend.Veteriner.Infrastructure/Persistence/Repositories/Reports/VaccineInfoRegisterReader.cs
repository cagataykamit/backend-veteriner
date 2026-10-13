using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Reports.Registers.VaccineInfo;
using Backend.Veteriner.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Reports;

/// <summary>
/// Aşı bilgi raporu: aşı ürünlerinin uygulamaları, hayvan bazlı; seri no ve SKT uygulamanın partisinden (sol birleşim).
/// Fiyat ürünün güncel katalog fiyatıdır; toplam <c>Charge</c> ile aynı yuvarlama kuralıyla hesaplanır (sözleşme bölüm 16).
/// </summary>
public sealed class VaccineInfoRegisterReader : IRegisterReader<VaccineInfoRegisterItemDto>
{
    private readonly AppDbContext _db;

    public VaccineInfoRegisterReader(AppDbContext db)
        => _db = db;

    public async Task<RegisterReadResult<VaccineInfoRegisterItemDto>> ReadAsync(
        RegisterReadRequest request,
        CancellationToken ct = default)
    {
        var administrations = _db.Administrations.AsNoTracking()
            .Where(a => a.TenantId == request.TenantId
                        && a.AdministeredAtUtc >= request.FromUtc
                        && a.AdministeredAtUtc < request.ToUtcExclusive);

        if (!request.IncludeVoided)
            administrations = administrations.Where(a => a.VoidedAtUtc == null);
        if (request.ProductId is { } productId)
            administrations = administrations.Where(a => a.ProductId == productId);
        if (request.VeterinarianUserId is { } veterinarianId)
            administrations = administrations.Where(a => a.CreatedByUserId == veterinarianId);

        if (request.ClinicId.HasValue)
        {
            var clinicId = request.ClinicId.Value;
            administrations = administrations.Where(a => a.ClinicId == clinicId);
        }
        else if (request.AccessibleClinicIds is { } accessible)
        {
            administrations = accessible.Count == 0
                ? administrations.Where(_ => false)
                : administrations.Where(a => accessible.Contains(a.ClinicId));
        }

        var query =
            from a in administrations
            join p in _db.Products.AsNoTracking() on a.ProductId equals p.Id
            join pet in _db.Pets.AsNoTracking() on a.PetId equals pet.Id
            join c in _db.Clients.AsNoTracking() on pet.ClientId equals c.Id
            join s in _db.Species.AsNoTracking() on pet.SpeciesId equals s.Id
            join b in _db.StockBatches.AsNoTracking() on a.BatchId equals b.Id into batches
            from b in batches.DefaultIfEmpty()
            where p.TenantId == request.TenantId && p.Kind == ProductKind.Vaccine
            select new { a, p, pet, c, s, b };

        var totalCount = await query.CountAsync(ct);
        if (totalCount == 0 || request.Skip >= totalCount)
            return new RegisterReadResult<VaccineInfoRegisterItemDto>(totalCount, []);

        var rows = await query
            .OrderBy(x => x.a.AdministeredAtUtc).ThenBy(x => x.a.CreatedAtUtc).ThenBy(x => x.a.Id)
            .Skip(request.Skip).Take(request.Take)
            .Select(x => new
            {
                x.a.Id,
                x.a.AdministeredAtUtc,
                OwnerName = x.c.FullName,
                PetName = x.pet.Name,
                Species = x.s.Name,
                x.pet.MicrochipNumber,
                ProductName = x.p.Name,
                LotNumber = x.b == null ? null : x.b.LotNumber,
                ExpiryDate = x.b == null ? null : x.b.ExpiryDate,
                x.p.Unit,
                x.a.Quantity,
                x.p.UnitPrice,
                x.p.Currency,
                x.a.AddedAfterFinalization,
                IsVoided = x.a.VoidedAtUtc != null,
            })
            .ToListAsync(ct);

        var items = rows.Select((r, i) => new VaccineInfoRegisterItemDto(
            request.Skip + i + 1,
            r.Id,
            r.AdministeredAtUtc,
            r.OwnerName,
            ClientIdentityNumber: null,
            r.PetName,
            r.Species,
            r.MicrochipNumber,
            r.ProductName,
            r.LotNumber,
            r.ExpiryDate,
            r.Unit,
            r.Quantity,
            r.UnitPrice,
            decimal.Round(r.Quantity * r.UnitPrice, 2, MidpointRounding.AwayFromZero),
            r.Currency,
            r.AddedAfterFinalization,
            r.IsVoided)).ToList();

        return new RegisterReadResult<VaccineInfoRegisterItemDto>(totalCount, items);
    }
}
